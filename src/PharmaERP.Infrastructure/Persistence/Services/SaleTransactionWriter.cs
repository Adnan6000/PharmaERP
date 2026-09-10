using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class SaleTransactionWriter : ISaleTransactionWriter
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAppConfigRepository _appConfigRepository;
    private readonly IBusinessClock _businessClock;
    private readonly IAccountingOperationalGate _accountingGate;
    private readonly IAccountingTransactionWriter _accountingTransactionWriter;
    private readonly ILogger<SaleTransactionWriter> _logger;

    public SaleTransactionWriter(
        IDbContextFactory<AppDbContext> contextFactory,
        IAppConfigRepository appConfigRepository,
        IBusinessClock businessClock,
        IAccountingOperationalGate accountingGate,
        IAccountingTransactionWriter accountingTransactionWriter,
        ILogger<SaleTransactionWriter> logger)
    {
        _contextFactory = contextFactory;
        _appConfigRepository = appConfigRepository;
        _businessClock = businessClock;
        _accountingGate = accountingGate;
        _accountingTransactionWriter = accountingTransactionWriter;
        _logger = logger;
    }

    public async Task<SaleInvoiceDto> CreateAndPostSaleInvoiceAsync(
        string invoiceNumber,
        SaleInvoiceCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ValidationException("Invoice number is required.");

        if (dto.Items == null || dto.Items.Count == 0)
            throw new ValidationException("Sale invoice must contain at least one line item.");

        if (dto.SaleType == SaleType.Credit && !dto.CustomerId.HasValue)
            throw new ValidationException("A customer is required for credit sales.");

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Idempotency check before starting (Requirement 11)
        var existing = await context.SaleInvoices
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OperationId == dto.OperationId, cancellationToken);

        if (existing != null)
        {
            return MapToDto(existing);
        }

        // Validate customer if provided
        Customer? customer = null;
        if (dto.CustomerId.HasValue)
        {
            customer = await context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == dto.CustomerId.Value, cancellationToken)
                ?? throw new NotFoundException($"Customer with ID {dto.CustomerId.Value} was not found.");

            if (!customer.IsActive)
                throw new ValidationException($"Customer '{customer.Name}' is inactive.");
        }

        // Validate items basic constraints
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                throw new ValidationException("Item quantity must be greater than zero.");
            if (item.UnitSalePrice < 0)
                throw new ValidationException("Unit sale price cannot be negative.");
            if (item.LineDiscountAmount < 0)
                throw new ValidationException("Line discount cannot be negative.");
        }

        // Multi-Product Lock Order: ProductId ASC (Requirement 10)
        var sortedInputItems = dto.Items
            .Select((item, originalIndex) => new { Item = item, OriginalIndex = originalIndex })
            .OrderBy(x => x.Item.ProductId)
            .ThenBy(x => x.OriginalIndex)
            .ToList();

        // Server-Authoritative Business Time & Date (Requirement 9 & 1)
        DateTime serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        DateOnly serverBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        var businessDate = serverBusinessDate; // Client dto.BusinessDate is strictly ignored

        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Fetch current Business Profile for Historical Print Identity Snapshots (Requirement 12)
        var businessProfile = await _appConfigRepository.GetBusinessProfileAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

        try
        {
            // Pre-calculate line amounts and invoice discount proration
            decimal totalGross = 0m;
            decimal totalLineDiscount = 0m;
            var itemComputations = new List<(SaleInvoiceItemCreateDto Item, decimal Gross, decimal LineDiscount, decimal BaseAmount)>();

            foreach (var entry in sortedInputItems)
            {
                decimal gross = Math.Round(entry.Item.Quantity * entry.Item.UnitSalePrice, 4);
                decimal lineDisc = Math.Round(entry.Item.LineDiscountAmount, 4);
                decimal baseAmt = gross - lineDisc;
                totalGross += gross;
                totalLineDiscount += lineDisc;
                itemComputations.Add((entry.Item, gross, lineDisc, baseAmt));
            }

            decimal invoiceDiscount = Math.Round(dto.InvoiceDiscountAmount, 4);
            decimal totalBase = totalGross - totalLineDiscount;
            decimal netTotal = totalGross - totalLineDiscount - invoiceDiscount;

            if (dto.SaleType == SaleType.Cash && dto.TenderedAmount.HasValue && dto.TenderedAmount.Value < netTotal)
            {
                throw new ValidationException($"Tendered amount ({dto.TenderedAmount.Value:F2}) is less than net total ({netTotal:F2}).");
            }

            decimal? changeGiven = (dto.SaleType == SaleType.Cash && dto.TenderedAmount.HasValue)
                ? dto.TenderedAmount.Value - netTotal
                : null;

            // 1. Insert SaleInvoice Header
            var invoice = new SaleInvoice
            {
                InvoiceNumber = invoiceNumber,
                OperationId = dto.OperationId,
                InvoiceDateTimeUtc = serverUtc,
                BusinessDate = businessDate,
                CustomerId = dto.CustomerId,
                CustomerNameSnapshot = customer != null ? customer.Name : "Walk-in Customer",
                SaleType = dto.SaleType,
                Reference = dto.Reference?.Trim(),
                Remarks = dto.Remarks?.Trim(),
                GrossTotal = totalGross,
                LineDiscountTotal = totalLineDiscount,
                InvoiceDiscountAmount = invoiceDiscount,
                TaxAmount = 0.0000m,
                NetTotal = netTotal,
                TenderedAmount = dto.TenderedAmount,
                ChangeGiven = changeGiven,
                Status = SaleInvoiceStatus.Posted,
                PostedAtUtc = serverUtc,
                PharmacyNameSnapshot = businessProfile.PharmacyName,
                PharmacyAddressSnapshot = businessProfile.Address,
                PharmacyPhoneSnapshot = businessProfile.Phone,
                DrugLicenseNoSnapshot = businessProfile.DrugLicenseNumber,
                TaxNumberSnapshot = businessProfile.TaxNumber,
                ReceiptFooterSnapshot = businessProfile.ReceiptFooter,
                CreatedAtUtc = serverUtc
            };

            await context.SaleInvoices.AddAsync(invoice, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // Prorate invoice discount across items
            decimal allocatedDiscountSum = 0m;
            var invoiceItems = new List<SaleInvoiceItem>();

            for (int i = 0; i < itemComputations.Count; i++)
            {
                var comp = itemComputations[i];
                decimal allocatedInvDisc = 0m;
                if (totalBase > 0 && invoiceDiscount > 0)
                {
                    if (i == itemComputations.Count - 1)
                    {
                        allocatedInvDisc = invoiceDiscount - allocatedDiscountSum;
                    }
                    else
                    {
                        allocatedInvDisc = Math.Round(invoiceDiscount * (comp.BaseAmount / totalBase), 4);
                        allocatedDiscountSum += allocatedInvDisc;
                    }
                }

                decimal netLine = comp.Gross - comp.LineDiscount - allocatedInvDisc;

                var product = await context.Products
                    .Include(p => p.Unit)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == comp.Item.ProductId, cancellationToken)
                    ?? throw new NotFoundException($"Product with ID {comp.Item.ProductId} was not found.");

                if (!product.IsActive)
                    throw new ValidationException($"Product '{product.Name}' is inactive.");

                var invItem = new SaleInvoiceItem
                {
                    SaleInvoiceId = invoice.Id,
                    ProductId = comp.Item.ProductId,
                    ProductCodeSnapshot = product.ProductCode ?? string.Empty,
                    ProductNameSnapshot = product.Name,
                    UnitSnapshot = product.Unit?.Name ?? "Unit",
                    Quantity = comp.Item.Quantity,
                    UnitSalePrice = comp.Item.UnitSalePrice,
                    GrossAmount = comp.Gross,
                    LineDiscountAmount = comp.LineDiscount,
                    InvoiceDiscountAllocated = allocatedInvDisc,
                    TaxAllocated = 0.0000m,
                    NetLineAmount = netLine,
                    ReturnedQuantity = 0.0000m,
                    RefundedAmount = 0.0000m,
                    CreatedAtUtc = serverUtc
                };

                await context.SaleInvoiceItems.AddAsync(invItem, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                invoiceItems.Add(invItem);

                // Transaction-Time FEFO Allocation & Atomic Stock Decrement (Requirements 1, 2, 5, 7, 8, 9)
                List<BatchCandidate> eligibleBatches;
                if (comp.Item.ExplicitBatchId.HasValue)
                {
                    // Cashier manual override
                    eligibleBatches = await GetBatchesForExplicitOverrideAsync(
                        connection, dbTransaction, comp.Item.ProductId, comp.Item.ExplicitBatchId.Value, businessDate, cancellationToken);
                }
                else
                {
                    // Automatic FEFO query inside SQL transaction with UPDLOCK, ROWLOCK (ExpiryDate ASC, Id ASC)
                    eligibleBatches = await GetEligibleBatchesFefoAsync(
                        connection, dbTransaction, comp.Item.ProductId, businessDate, cancellationToken);
                }

                decimal totalAvailableStock = eligibleBatches.Sum(b => b.QuantityOnHand);
                if (totalAvailableStock < comp.Item.Quantity)
                {
                    throw new InsufficientStockException(
                        $"Insufficient unexpired stock for product '{product.Name}'. Required: {comp.Item.Quantity}, Available: {totalAvailableStock}.");
                }

                decimal remainingToAllocate = comp.Item.Quantity;
                foreach (var batch in eligibleBatches)
                {
                    if (remainingToAllocate <= 0) break;

                    decimal deductQty = Math.Min(remainingToAllocate, batch.QuantityOnHand);

                    // Execute Atomic SQL Stock Outflow with OUTPUT capturing pre-sale cost and exact value consumed (Requirements 1, 2, 7)
                    var mutationResult = await ExecuteAtomicStockDecrementAsync(
                        connection, dbTransaction, batch.Id, deductQty, cancellationToken);

                    // Requirement 1:
                    // Persist:
                    // SaleInvoiceBatchAllocation.InventoryCostRate = PreSaleAverageCost
                    // SaleInvoiceBatchAllocation.InventoryValueConsumed = OldInventoryValue - NewInventoryValue
                    // StockMovement.InventoryCostRate = PreSaleAverageCost
                    // StockMovement.InventoryValueDelta = -(OldInventoryValue - NewInventoryValue)
                    var allocation = new SaleInvoiceBatchAllocation
                    {
                        SaleInvoiceItemId = invItem.Id,
                        ProductBatchId = batch.Id,
                        Quantity = deductQty,
                        InventoryCostRate = mutationResult.PreSaleAverageCost,
                        InventoryValueConsumed = mutationResult.ValueConsumed,
                        BatchNumberSnapshot = batch.BatchNumber,
                        ExpiryDateSnapshot = batch.ExpiryDate,
                        ReturnedQuantity = 0.0000m,
                        ReturnedInventoryValue = 0.0000m,
                        CreatedAtUtc = serverUtc
                    };

                    await context.SaleInvoiceBatchAllocations.AddAsync(allocation, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    var movement = new StockMovement
                    {
                        ProductId = comp.Item.ProductId,
                        ProductBatchId = batch.Id,
                        MovementType = StockMovementType.Sale,
                        QuantityDelta = -deductQty,
                        BalanceAfter = mutationResult.NewQuantity,
                        UnitCost = comp.Item.UnitSalePrice,
                        InventoryCostRate = mutationResult.PreSaleAverageCost,
                        InventoryValueDelta = -mutationResult.ValueConsumed,
                        InventoryValueAfter = mutationResult.NewInventoryValue,
                        ReferenceDocumentType = StockReferenceDocumentType.SaleInvoice,
                        ReferenceDocumentId = invoice.Id,
                        ReferenceDocumentItemId = invItem.Id,
                        ReferenceDocumentNumber = invoice.InvoiceNumber,
                        TransactionDateUtc = serverUtc,
                        Remarks = $"Sale: {invoice.InvoiceNumber} - Line: {invItem.ProductCodeSnapshot}",
                        CreatedAtUtc = serverUtc
                    };

                    await context.StockMovements.AddAsync(movement, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    if (!invItem.BatchAllocations.Contains(allocation))
                    {
                        invItem.BatchAllocations.Add(allocation);
                    }
                    remainingToAllocate -= deductQty;
                }
            }

            decimal totalCogs = invoiceItems.SelectMany(i => i.BatchAllocations).Sum(a => a.InventoryValueConsumed);
            await _accountingTransactionWriter.PostSaleInvoiceJournalAsync(context, invoice, totalCogs, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            invoice.Items = invoiceItems;
            return MapToDto(invoice);
        }
        catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            // Re-query for idempotency race condition (Requirement 11)
            var duplicate = await context.SaleInvoices
                .Include(s => s.Items)
                    .ThenInclude(i => i.BatchAllocations)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.OperationId == dto.OperationId, cancellationToken);

            if (duplicate != null)
            {
                _logger.LogInformation("Recovered existing SaleInvoice on unique constraint race for OperationId {OperationId}", dto.OperationId);
                return MapToDto(duplicate);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed for SaleInvoice {InvoiceNumber}. Rolling back.", invoiceNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SaleReturnDto> CreateAndPostSaleReturnAsync(
        string returnNumber,
        SaleReturnCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(returnNumber))
            throw new ValidationException("Return number is required.");

        if (dto.Items == null || dto.Items.Count == 0)
            throw new ValidationException("Sale return must contain at least one line item.");

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Idempotency check before starting (Requirement 11)
        var existing = await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperationId == dto.OperationId, cancellationToken);

        if (existing != null)
        {
            return MapReturnToDto(existing);
        }

        var invoice = await context.SaleInvoices
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == dto.OriginalSaleInvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Sale invoice with ID {dto.OriginalSaleInvoiceId} was not found.");

        if (invoice.Status != SaleInvoiceStatus.Posted)
            throw new ValidationException("Only posted sales invoices can receive customer returns.");

        // Server-Authoritative Business Time & Date (Requirement 9 & 1)
        DateTime serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        DateOnly serverBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        var returnDate = serverBusinessDate; // Client dto.ReturnDate is strictly ignored

        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

        try
        {
            var returnEntity = new SaleReturn
            {
                ReturnNumber = returnNumber,
                OperationId = dto.OperationId,
                OriginalSaleInvoiceId = invoice.Id,
                CustomerId = invoice.CustomerId,
                CustomerNameSnapshot = invoice.CustomerNameSnapshot,
                ReturnDate = returnDate,
                Reason = dto.Reason?.Trim(),
                TotalAmount = 0m,
                Status = SaleReturnStatus.Posted,
                PostedAtUtc = serverUtc,
                CreatedAtUtc = serverUtc
            };

            await context.SaleReturns.AddAsync(returnEntity, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            decimal totalRefundAmount = 0m;
            var returnItems = new List<SaleReturnItem>();

            // Sort return items by ProductId ASC for deterministic lock ordering (Requirement 10)
            var sortedReturnItems = dto.Items
                .Select(item =>
                {
                    var originalItem = invoice.Items.FirstOrDefault(i => i.Id == item.OriginalSaleInvoiceItemId)
                        ?? throw new NotFoundException($"Invoice line item {item.OriginalSaleInvoiceItemId} not found on invoice.");
                    return new { ReturnItem = item, OriginalItem = originalItem };
                })
                .OrderBy(x => x.OriginalItem.ProductId)
                .ThenBy(x => x.OriginalItem.Id)
                .ToList();

            foreach (var entry in sortedReturnItems)
            {
                var retItemInput = entry.ReturnItem;
                var origItem = entry.OriginalItem;

                if (retItemInput.Quantity <= 0)
                    throw new ValidationException("Return quantity must be greater than zero.");

                // Check remaining returnable quantity in memory
                decimal unreturnedQty = origItem.Quantity - origItem.ReturnedQuantity;
                if (retItemInput.Quantity > unreturnedQty)
                {
                    throw new ValidationException(
                        $"Cannot return {retItemInput.Quantity} units for product '{origItem.ProductNameSnapshot}'. Max returnable: {unreturnedQty}.");
                }

                // Calculate Financial Refund (Requirement 6 & 12)
                decimal refundAmount;
                bool isFinalReturnForLine = (origItem.ReturnedQuantity + retItemInput.Quantity == origItem.Quantity);
                if (isFinalReturnForLine)
                {
                    // Final eligible return absorbs remaining rounding: NetLineAmount - RefundedAmount
                    refundAmount = origItem.NetLineAmount - origItem.RefundedAmount;
                }
                else
                {
                    decimal unitRefundRate = origItem.NetLineAmount / origItem.Quantity;
                    refundAmount = Math.Round(retItemInput.Quantity * unitRefundRate, 4);
                }

                // Atomic Financial & Quantity DB Guard (Requirement 6)
                await ExecuteAtomicItemReturnGuardAsync(
                    connection, dbTransaction, origItem.Id, retItemInput.Quantity, refundAmount, cancellationToken);

                var returnItemEntity = new SaleReturnItem
                {
                    SaleReturnId = returnEntity.Id,
                    OriginalSaleInvoiceItemId = origItem.Id,
                    ProductId = origItem.ProductId,
                    Quantity = retItemInput.Quantity,
                    RefundAmount = refundAmount,
                    CreatedAtUtc = serverUtc
                };

                await context.SaleReturnItems.AddAsync(returnItemEntity, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                returnItems.Add(returnItemEntity);
                totalRefundAmount += refundAmount;

                // Allocate return to exact original batches (Requirement 3, 4, 6)
                decimal remainingReturnQty = retItemInput.Quantity;
                var origAllocations = origItem.BatchAllocations
                    .OrderBy(a => a.ExpiryDateSnapshot)
                    .ThenBy(a => a.Id)
                    .ToList();

                foreach (var alloc in origAllocations)
                {
                    if (remainingReturnQty <= 0) break;

                    decimal allocAvailableReturn = alloc.Quantity - alloc.ReturnedQuantity;
                    if (allocAvailableReturn <= 0) continue;

                    decimal takeAllocReturn = Math.Min(remainingReturnQty, allocAvailableReturn);

                    // Exact Inventory Value Restoration calculation (Requirement 3)
                    decimal valueToRestore;
                    bool isFinalAllocReturn = (alloc.ReturnedQuantity + takeAllocReturn == alloc.Quantity);
                    if (isFinalAllocReturn)
                    {
                        // Exact remaining unreturned InventoryValueConsumed
                        valueToRestore = alloc.InventoryValueConsumed - alloc.ReturnedInventoryValue;
                    }
                    else
                    {
                        valueToRestore = Math.Round(alloc.InventoryValueConsumed * (takeAllocReturn / alloc.Quantity), 4);
                    }

                    // Atomic Allocation DB Guard (Requirement 3)
                    await ExecuteAtomicAllocationReturnGuardAsync(
                        connection, dbTransaction, alloc.Id, takeAllocReturn, valueToRestore, cancellationToken);

                    // Restore stock to exact original ProductBatch (Requirement 3 & 4)
                    await ExecuteAtomicStockInflowAsync(
                        connection, dbTransaction, alloc.ProductBatchId, takeAllocReturn, valueToRestore, cancellationToken);

                    var returnBatchAlloc = new SaleReturnBatchAllocation
                    {
                        SaleReturnItemId = returnItemEntity.Id,
                        OriginalSaleInvoiceBatchAllocationId = alloc.Id,
                        ProductBatchId = alloc.ProductBatchId,
                        Quantity = takeAllocReturn,
                        RestoredInventoryCostRate = alloc.InventoryCostRate,
                        RestoredInventoryValue = valueToRestore,
                        CreatedAtUtc = serverUtc
                    };

                    await context.SaleReturnBatchAllocations.AddAsync(returnBatchAlloc, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    // Stock movement for return
                    var moveCmd = connection.CreateCommand();
                    moveCmd.Transaction = dbTransaction;
                    moveCmd.CommandText = "SELECT QuantityOnHand, InventoryValue FROM ProductBatches WHERE Id = @bId";
                    moveCmd.Parameters.AddWithValue("@bId", alloc.ProductBatchId);
                    decimal currentQty, currentVal;
                    using (var rdr = await moveCmd.ExecuteReaderAsync(cancellationToken))
                    {
                        await rdr.ReadAsync(cancellationToken);
                        currentQty = rdr.GetDecimal(0);
                        currentVal = rdr.GetDecimal(1);
                    }

                    var returnMovement = new StockMovement
                    {
                        ProductId = origItem.ProductId,
                        ProductBatchId = alloc.ProductBatchId,
                        MovementType = StockMovementType.SaleReturn,
                        QuantityDelta = takeAllocReturn,
                        BalanceAfter = currentQty,
                        UnitCost = refundAmount / retItemInput.Quantity,
                        InventoryCostRate = alloc.InventoryCostRate,
                        InventoryValueDelta = valueToRestore,
                        InventoryValueAfter = currentVal,
                        ReferenceDocumentType = StockReferenceDocumentType.SaleReturn,
                        ReferenceDocumentId = returnEntity.Id,
                        ReferenceDocumentItemId = returnItemEntity.Id,
                        ReferenceDocumentNumber = returnEntity.ReturnNumber,
                        TransactionDateUtc = serverUtc,
                        Remarks = $"Sale Return: {returnEntity.ReturnNumber} against {invoice.InvoiceNumber}",
                        CreatedAtUtc = serverUtc
                    };

                    await context.StockMovements.AddAsync(returnMovement, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    if (!returnItemEntity.BatchAllocations.Contains(returnBatchAlloc))
                    {
                        returnItemEntity.BatchAllocations.Add(returnBatchAlloc);
                    }
                    remainingReturnQty -= takeAllocReturn;
                }
            }

            returnEntity.TotalAmount = totalRefundAmount;
            returnEntity.Items = returnItems;
            await context.SaveChangesAsync(cancellationToken);

            decimal totalRestoredInv = returnItems.SelectMany(i => i.BatchAllocations).Sum(a => a.RestoredInventoryValue);
            await _accountingTransactionWriter.PostSaleReturnJournalAsync(context, returnEntity, totalRestoredInv, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return MapReturnToDto(returnEntity);
        }
        catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            var duplicate = await context.SaleReturns
                .Include(r => r.OriginalSaleInvoice)
                .Include(r => r.Items)
                    .ThenInclude(i => i.BatchAllocations)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.OperationId == dto.OperationId, cancellationToken);

            if (duplicate != null)
            {
                _logger.LogInformation("Recovered existing SaleReturn on unique constraint race for OperationId {OperationId}", dto.OperationId);
                return MapReturnToDto(duplicate);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed for SaleReturn {ReturnNumber}. Rolling back.", returnNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SaleInvoiceDto> CancelSaleInvoiceAsync(
        int id,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var invoice = await context.SaleInvoices
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Sale invoice with ID {id} was not found.");

        if (invoice.Status == SaleInvoiceStatus.Cancelled)
            throw new ValidationException($"Sale invoice '{invoice.InvoiceNumber}' is already cancelled.");

        if (invoice.Status != SaleInvoiceStatus.Posted)
            throw new ValidationException($"Only posted invoices can be cancelled. Current status: {invoice.Status}.");

        // Guard: Verify no items have returned quantities (Requirement 5)
        if (invoice.Items.Any(i => i.ReturnedQuantity > 0))
        {
            throw new ValidationException(
                "This sale invoice has customer returns recorded against it. Cancel those returns first, then try cancelling the sale.");
        }

        // Guard: Verify no active (non-cancelled) SaleReturn exists for this invoice (Requirement 5)
        bool hasActiveReturns = await context.SaleReturns
            .AnyAsync(r => r.OriginalSaleInvoiceId == id && r.Status != SaleReturnStatus.Cancelled, cancellationToken);

        if (hasActiveReturns)
        {
            throw new ValidationException(
                "This sale invoice has active customer returns recorded against it. Cancel those returns first, then try cancelling the sale.");
        }

        // Guard: Verify no active receipt voucher settlement allocations exist for this invoice
        bool hasActiveAllocations = await context.ReceiptVoucherAllocations
            .AnyAsync(a => a.SaleInvoiceId == id && a.Status == AllocationStatus.Active, cancellationToken);

        if (hasActiveAllocations)
        {
            throw new ValidationException(
                $"Cannot cancel sale invoice '{invoice.InvoiceNumber}' because it has active receipt voucher settlement allocations. Remove or void allocations first.");
        }


        DateTime serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);

        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

        try
        {
            // Sale cancellation is an INFLOW! Restores exact sold quantity and original InventoryValueConsumed (Requirement 5)
            foreach (var item in invoice.Items)
            {
                foreach (var alloc in item.BatchAllocations)
                {
                    // Restore stock and exact InventoryValueConsumed to ProductBatch
                    await ExecuteAtomicStockInflowAsync(
                        connection, dbTransaction, alloc.ProductBatchId, alloc.Quantity, alloc.InventoryValueConsumed, cancellationToken);

                    // Fetch original sale StockMovement
                    var origMovement = await context.StockMovements
                        .FirstOrDefaultAsync(m => m.ReferenceDocumentType == StockReferenceDocumentType.SaleInvoice &&
                                                  m.ReferenceDocumentId == invoice.Id &&
                                                  m.ReferenceDocumentItemId == item.Id &&
                                                  m.ProductBatchId == alloc.ProductBatchId, cancellationToken);

                    var moveCmd = connection.CreateCommand();
                    moveCmd.Transaction = dbTransaction;
                    moveCmd.CommandText = "SELECT QuantityOnHand, InventoryValue FROM ProductBatches WHERE Id = @bId";
                    moveCmd.Parameters.AddWithValue("@bId", alloc.ProductBatchId);
                    decimal curQty, curVal;
                    using (var rdr = await moveCmd.ExecuteReaderAsync(cancellationToken))
                    {
                        await rdr.ReadAsync(cancellationToken);
                        curQty = rdr.GetDecimal(0);
                        curVal = rdr.GetDecimal(1);
                    }

                    var reversalMovement = new StockMovement
                    {
                        ProductId = item.ProductId,
                        ProductBatchId = alloc.ProductBatchId,
                        MovementType = StockMovementType.SaleReversal,
                        QuantityDelta = alloc.Quantity,
                        BalanceAfter = curQty,
                        UnitCost = item.UnitSalePrice,
                        InventoryCostRate = alloc.InventoryCostRate,
                        InventoryValueDelta = alloc.InventoryValueConsumed,
                        InventoryValueAfter = curVal,
                        ReferenceDocumentType = StockReferenceDocumentType.SaleInvoice,
                        ReferenceDocumentId = invoice.Id,
                        ReferenceDocumentItemId = item.Id,
                        ReferenceDocumentNumber = invoice.InvoiceNumber,
                        ReversesStockMovementId = origMovement?.Id,
                        TransactionDateUtc = serverUtc,
                        Remarks = $"Sale Reversal: Cancelled {invoice.InvoiceNumber} - {cancellationReason}",
                        CreatedAtUtc = serverUtc
                    };

                    await context.StockMovements.AddAsync(reversalMovement, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            invoice.Status = SaleInvoiceStatus.Cancelled;
            invoice.CancelledAtUtc = serverUtc;
            invoice.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostSaleInvoiceReversalAsync(context, invoice, cancellationReason, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return MapToDto(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel SaleInvoice {InvoiceNumber}. Rolling back.", invoice.InvoiceNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SaleReturnDto> CancelSaleReturnAsync(
        int id,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var saleReturn = await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Sale return with ID {id} was not found.");

        if (saleReturn.Status == SaleReturnStatus.Cancelled)
            throw new ValidationException($"Sale return '{saleReturn.ReturnNumber}' is already cancelled.");

        if (saleReturn.Status != SaleReturnStatus.Posted)
            throw new ValidationException($"Only posted sales returns can be cancelled. Current status: {saleReturn.Status}.");

        DateTime serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);

        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Downstream Consumption Guard (Requirement 4 & 16)
        foreach (var item in saleReturn.Items)
        {
            foreach (var alloc in item.BatchAllocations)
            {
                var returnMovement = await context.StockMovements
                    .FirstOrDefaultAsync(m => m.ReferenceDocumentType == StockReferenceDocumentType.SaleReturn &&
                                              m.ReferenceDocumentId == saleReturn.Id &&
                                              m.ReferenceDocumentItemId == item.Id &&
                                              m.ProductBatchId == alloc.ProductBatchId, cancellationToken);

                if (returnMovement != null)
                {
                    bool hasDownstreamOutflow = await context.StockMovements.AnyAsync(m =>
                        m.ProductBatchId == alloc.ProductBatchId &&
                        m.CreatedAtUtc > returnMovement.CreatedAtUtc &&
                        m.QuantityDelta < 0 &&
                        !context.StockMovements.Any(rev => rev.ReversesStockMovementId == m.Id), cancellationToken);

                    if (hasDownstreamOutflow)
                    {
                        var batch = await context.ProductBatches.FindAsync(alloc.ProductBatchId);
                        throw new ValidationException(
                            $"Cannot cancel sales return: stock returned to batch '{batch?.BatchNumber ?? alloc.ProductBatchId.ToString()}' has already been consumed by subsequent sales or adjustments.");
                    }
                }

                // Check sufficient stock on batch
                var curBatch = await context.ProductBatches.FindAsync(alloc.ProductBatchId);
                if (curBatch == null || curBatch.QuantityOnHand < alloc.Quantity)
                {
                    throw new ValidationException(
                        $"Cannot cancel sales return: batch '{curBatch?.BatchNumber}' only has {curBatch?.QuantityOnHand ?? 0} units remaining, but {alloc.Quantity} units must be removed.");
                }
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

        try
        {
            // Execute Outflow: Deduct returned stock and remove EXACT RestoredInventoryValue (Requirement 4)
            foreach (var item in saleReturn.Items)
            {
                // Atomically decrement item counters
                var decItemCmd = connection.CreateCommand();
                decItemCmd.Transaction = dbTransaction;
                decItemCmd.CommandText = @"
                    UPDATE SaleInvoiceItems
                    SET ReturnedQuantity = ReturnedQuantity - @qty,
                        RefundedAmount = RefundedAmount - @refund
                    WHERE Id = @id;";
                decItemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                decItemCmd.Parameters.AddWithValue("@refund", item.RefundAmount);
                decItemCmd.Parameters.AddWithValue("@id", item.OriginalSaleInvoiceItemId);
                await decItemCmd.ExecuteNonQueryAsync(cancellationToken);

                foreach (var alloc in item.BatchAllocations)
                {
                    // Atomically decrement allocation counters
                    var decAllocCmd = connection.CreateCommand();
                    decAllocCmd.Transaction = dbTransaction;
                    decAllocCmd.CommandText = @"
                        UPDATE SaleInvoiceBatchAllocations
                        SET ReturnedQuantity = ReturnedQuantity - @qty,
                            ReturnedInventoryValue = ReturnedInventoryValue - @val
                        WHERE Id = @id;";
                    decAllocCmd.Parameters.AddWithValue("@qty", alloc.Quantity);
                    decAllocCmd.Parameters.AddWithValue("@val", alloc.RestoredInventoryValue);
                    decAllocCmd.Parameters.AddWithValue("@id", alloc.OriginalSaleInvoiceBatchAllocationId);
                    await decAllocCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Outflow from batch removing EXACT RestoredInventoryValue and recalculating AveragePurchaseCost (Requirement 4)
                    await ExecuteExactStockOutflowAsync(
                        connection, dbTransaction, alloc.ProductBatchId, alloc.Quantity, alloc.RestoredInventoryValue, cancellationToken);

                    var origReturnMovement = await context.StockMovements
                        .FirstOrDefaultAsync(m => m.ReferenceDocumentType == StockReferenceDocumentType.SaleReturn &&
                                                  m.ReferenceDocumentId == saleReturn.Id &&
                                                  m.ReferenceDocumentItemId == item.Id &&
                                                  m.ProductBatchId == alloc.ProductBatchId, cancellationToken);

                    var moveCmd = connection.CreateCommand();
                    moveCmd.Transaction = dbTransaction;
                    moveCmd.CommandText = "SELECT QuantityOnHand, InventoryValue FROM ProductBatches WHERE Id = @bId";
                    moveCmd.Parameters.AddWithValue("@bId", alloc.ProductBatchId);
                    decimal curQty, curVal;
                    using (var rdr = await moveCmd.ExecuteReaderAsync(cancellationToken))
                    {
                        await rdr.ReadAsync(cancellationToken);
                        curQty = rdr.GetDecimal(0);
                        curVal = rdr.GetDecimal(1);
                    }

                    var reversalMovement = new StockMovement
                    {
                        ProductId = item.ProductId,
                        ProductBatchId = alloc.ProductBatchId,
                        MovementType = StockMovementType.SaleReturnReversal,
                        QuantityDelta = -alloc.Quantity,
                        BalanceAfter = curQty,
                        UnitCost = item.RefundAmount / item.Quantity,
                        InventoryCostRate = alloc.RestoredInventoryCostRate,
                        InventoryValueDelta = -alloc.RestoredInventoryValue,
                        InventoryValueAfter = curVal,
                        ReferenceDocumentType = StockReferenceDocumentType.SaleReturn,
                        ReferenceDocumentId = saleReturn.Id,
                        ReferenceDocumentItemId = item.Id,
                        ReferenceDocumentNumber = saleReturn.ReturnNumber,
                        ReversesStockMovementId = origReturnMovement?.Id,
                        TransactionDateUtc = serverUtc,
                        Remarks = $"Sale Return Reversal: Cancelled {saleReturn.ReturnNumber} - {cancellationReason}",
                        CreatedAtUtc = serverUtc
                    };

                    await context.StockMovements.AddAsync(reversalMovement, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            saleReturn.Status = SaleReturnStatus.Cancelled;
            saleReturn.CancelledAtUtc = serverUtc;
            saleReturn.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostSaleReturnReversalAsync(context, saleReturn, cancellationReason, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return MapReturnToDto(saleReturn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel SaleReturn {ReturnNumber}. Rolling back.", saleReturn.ReturnNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    #region Helper Execution Methods

    private static async Task<List<BatchCandidate>> GetEligibleBatchesFefoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT Id, BatchNumber, ExpiryDate, QuantityOnHand, InventoryValue, AveragePurchaseCost
            FROM ProductBatches WITH (UPDLOCK, ROWLOCK)
            WHERE ProductId = @pId
              AND IsActive = 1
              AND QuantityOnHand > 0
              AND ExpiryDate > @busDate
            ORDER BY ExpiryDate ASC, Id ASC;";

        cmd.Parameters.AddWithValue("@pId", productId);
        cmd.Parameters.AddWithValue("@busDate", businessDate.ToDateTime(TimeOnly.MinValue));

        var candidates = new List<BatchCandidate>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new BatchCandidate
            {
                Id = reader.GetInt32(0),
                BatchNumber = reader.GetString(1),
                ExpiryDate = DateOnly.FromDateTime(reader.GetDateTime(2)),
                QuantityOnHand = reader.GetDecimal(3),
                InventoryValue = reader.GetDecimal(4),
                AveragePurchaseCost = reader.GetDecimal(5)
            });
        }

        return candidates;
    }

    private static async Task<List<BatchCandidate>> GetBatchesForExplicitOverrideAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productId,
        int explicitBatchId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT Id, BatchNumber, ExpiryDate, QuantityOnHand, InventoryValue, AveragePurchaseCost, IsActive
            FROM ProductBatches WITH (UPDLOCK, ROWLOCK)
            WHERE Id = @bId AND ProductId = @pId;";

        cmd.Parameters.AddWithValue("@bId", explicitBatchId);
        cmd.Parameters.AddWithValue("@pId", productId);

        var candidates = new List<BatchCandidate>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            int id = reader.GetInt32(0);
            string batchNum = reader.GetString(1);
            DateOnly exp = DateOnly.FromDateTime(reader.GetDateTime(2));
            decimal qty = reader.GetDecimal(3);
            decimal val = reader.GetDecimal(4);
            decimal avg = reader.GetDecimal(5);
            bool isActive = reader.GetBoolean(6);

            if (!isActive)
                throw new ValidationException($"Batch '{batchNum}' is inactive.");

            if (exp <= businessDate)
                throw new ValidationException($"Batch '{batchNum}' has expired ({exp:yyyy-MM-dd}) and cannot be sold.");

            candidates.Add(new BatchCandidate
            {
                Id = id,
                BatchNumber = batchNum,
                ExpiryDate = exp,
                QuantityOnHand = qty,
                InventoryValue = val,
                AveragePurchaseCost = avg
            });
        }
        else
        {
            throw new NotFoundException($"Batch with ID {explicitBatchId} does not belong to product {productId}.");
        }

        return candidates;
    }

    private static async Task<StockMutationOutput> ExecuteAtomicStockDecrementAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int batchId,
        decimal qty,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            DECLARE @Deltas TABLE (
                OldQty decimal(18,4),
                NewQty decimal(18,4),
                OldVal decimal(18,4),
                NewVal decimal(18,4),
                OldAvg decimal(18,4),
                NewAvg decimal(18,4)
            );

            UPDATE ProductBatches
            SET QuantityOnHand = QuantityOnHand - @qty,
                InventoryValue = CASE 
                    WHEN QuantityOnHand - @qty = 0 THEN 0.0000 
                    ELSE ROUND(InventoryValue - (@qty * AveragePurchaseCost), 4) 
                END,
                AveragePurchaseCost = CASE 
                    WHEN QuantityOnHand - @qty = 0 THEN 0.0000 
                    ELSE ROUND(ROUND(InventoryValue - (@qty * AveragePurchaseCost), 4) / (QuantityOnHand - @qty), 4) 
                END
            OUTPUT 
                DELETED.QuantityOnHand, INSERTED.QuantityOnHand,
                DELETED.InventoryValue, INSERTED.InventoryValue,
                DELETED.AveragePurchaseCost, INSERTED.AveragePurchaseCost
            INTO @Deltas
            WHERE Id = @batchId
              AND QuantityOnHand >= @qty;

            SELECT OldQty, NewQty, OldVal, NewVal, OldAvg, NewAvg FROM @Deltas;";

        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@batchId", batchId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InsufficientStockException($"Batch ID {batchId} does not have sufficient stock ({qty}) to fulfill sale.");
        }

        decimal oldQty = reader.GetDecimal(0);
        decimal newQty = reader.GetDecimal(1);
        decimal oldVal = reader.GetDecimal(2);
        decimal newVal = reader.GetDecimal(3);
        decimal oldAvg = reader.GetDecimal(4);
        decimal newAvg = reader.GetDecimal(5);

        return new StockMutationOutput
        {
            OldQuantity = oldQty,
            NewQuantity = newQty,
            OldInventoryValue = oldVal,
            NewInventoryValue = newVal,
            PreSaleAverageCost = oldAvg,
            PostSaleAverageCost = newAvg,
            ValueConsumed = oldVal - newVal
        };
    }

    private static async Task ExecuteAtomicStockInflowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int batchId,
        decimal qty,
        decimal valueToRestore,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE ProductBatches
            SET QuantityOnHand = QuantityOnHand + @qty,
                InventoryValue = InventoryValue + @val,
                AveragePurchaseCost = ROUND((InventoryValue + @val) / (QuantityOnHand + @qty), 4)
            WHERE Id = @batchId;";

        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@val", valueToRestore);
        cmd.Parameters.AddWithValue("@batchId", batchId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            throw new InvalidOperationException($"Failed to restore stock to batch ID {batchId}.");
        }
    }

    private static async Task ExecuteExactStockOutflowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int batchId,
        decimal qty,
        decimal exactValueToRemove,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE ProductBatches
            SET QuantityOnHand = QuantityOnHand - @qty,
                InventoryValue = CASE 
                    WHEN QuantityOnHand - @qty = 0 THEN 0.0000 
                    ELSE InventoryValue - @val 
                END,
                AveragePurchaseCost = CASE 
                    WHEN QuantityOnHand - @qty = 0 THEN 0.0000 
                    ELSE ROUND((InventoryValue - @val) / (QuantityOnHand - @qty), 4) 
                END
            WHERE Id = @batchId
              AND QuantityOnHand >= @qty;";

        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@val", exactValueToRemove);
        cmd.Parameters.AddWithValue("@batchId", batchId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            throw new InsufficientStockException($"Batch ID {batchId} does not have sufficient stock to reverse return.");
        }
    }

    private static async Task ExecuteAtomicItemReturnGuardAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int itemId,
        decimal returnQty,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE SaleInvoiceItems
            SET ReturnedQuantity = ReturnedQuantity + @qty,
                RefundedAmount = RefundedAmount + @refund
            WHERE Id = @itemId
              AND ReturnedQuantity + @qty <= Quantity
              AND RefundedAmount + @refund <= NetLineAmount;";

        cmd.Parameters.AddWithValue("@qty", returnQty);
        cmd.Parameters.AddWithValue("@refund", refundAmount);
        cmd.Parameters.AddWithValue("@itemId", itemId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            throw new ValidationException("Return exceeds allowable quantity or maximum refundable amount for this line item.");
        }
    }

    private static async Task ExecuteAtomicAllocationReturnGuardAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int allocationId,
        decimal returnQty,
        decimal valueToRestore,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE SaleInvoiceBatchAllocations
            SET ReturnedQuantity = ReturnedQuantity + @qty,
                ReturnedInventoryValue = ReturnedInventoryValue + @val
            WHERE Id = @allocId
              AND ReturnedQuantity + @qty <= Quantity
              AND ReturnedInventoryValue + @val <= InventoryValueConsumed;";

        cmd.Parameters.AddWithValue("@qty", returnQty);
        cmd.Parameters.AddWithValue("@val", valueToRestore);
        cmd.Parameters.AddWithValue("@allocId", allocationId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
        {
            throw new ValidationException("Return exceeds allowable quantity or inventory value for this batch allocation.");
        }
    }

    #endregion

    private static SaleInvoiceDto MapToDto(SaleInvoice invoice)
    {
        return new SaleInvoiceDto
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            OperationId = invoice.OperationId,
            InvoiceDateTimeUtc = invoice.InvoiceDateTimeUtc,
            BusinessDate = invoice.BusinessDate,
            CustomerId = invoice.CustomerId,
            CustomerNameSnapshot = invoice.CustomerNameSnapshot,
            SaleType = invoice.SaleType,
            Reference = invoice.Reference,
            Remarks = invoice.Remarks,
            GrossTotal = invoice.GrossTotal,
            LineDiscountTotal = invoice.LineDiscountTotal,
            InvoiceDiscountAmount = invoice.InvoiceDiscountAmount,
            TaxAmount = invoice.TaxAmount,
            NetTotal = invoice.NetTotal,
            TenderedAmount = invoice.TenderedAmount,
            ChangeGiven = invoice.ChangeGiven,
            Status = invoice.Status,
            PostedAtUtc = invoice.PostedAtUtc,
            CancelledAtUtc = invoice.CancelledAtUtc,
            CancellationReason = invoice.CancellationReason,
            PharmacyNameSnapshot = invoice.PharmacyNameSnapshot,
            PharmacyAddressSnapshot = invoice.PharmacyAddressSnapshot,
            PharmacyPhoneSnapshot = invoice.PharmacyPhoneSnapshot,
            DrugLicenseNoSnapshot = invoice.DrugLicenseNoSnapshot,
            TaxNumberSnapshot = invoice.TaxNumberSnapshot,
            ReceiptFooterSnapshot = invoice.ReceiptFooterSnapshot,
            Items = invoice.Items.Select(i => new SaleInvoiceItemDto
            {
                Id = i.Id,
                SaleInvoiceId = i.SaleInvoiceId,
                ProductId = i.ProductId,
                ProductCodeSnapshot = i.ProductCodeSnapshot,
                ProductNameSnapshot = i.ProductNameSnapshot,
                UnitSnapshot = i.UnitSnapshot,
                Quantity = i.Quantity,
                UnitSalePrice = i.UnitSalePrice,
                GrossAmount = i.GrossAmount,
                LineDiscountAmount = i.LineDiscountAmount,
                InvoiceDiscountAllocated = i.InvoiceDiscountAllocated,
                TaxAllocated = i.TaxAllocated,
                NetLineAmount = i.NetLineAmount,
                ReturnedQuantity = i.ReturnedQuantity,
                RefundedAmount = i.RefundedAmount,
                BatchAllocations = i.BatchAllocations.Select(b => new SaleInvoiceBatchAllocationDto
                {
                    Id = b.Id,
                    SaleInvoiceItemId = b.SaleInvoiceItemId,
                    ProductBatchId = b.ProductBatchId,
                    Quantity = b.Quantity,
                    InventoryCostRate = b.InventoryCostRate,
                    InventoryValueConsumed = b.InventoryValueConsumed,
                    BatchNumberSnapshot = b.BatchNumberSnapshot,
                    ExpiryDateSnapshot = b.ExpiryDateSnapshot,
                    ReturnedQuantity = b.ReturnedQuantity,
                    ReturnedInventoryValue = b.ReturnedInventoryValue
                }).ToList()
            }).ToList()
        };
    }

    private static SaleReturnDto MapReturnToDto(SaleReturn ret)
    {
        return new SaleReturnDto
        {
            Id = ret.Id,
            ReturnNumber = ret.ReturnNumber,
            OperationId = ret.OperationId,
            OriginalSaleInvoiceId = ret.OriginalSaleInvoiceId,
            OriginalSaleInvoiceNumber = ret.OriginalSaleInvoice?.InvoiceNumber ?? string.Empty,
            CustomerId = ret.CustomerId,
            CustomerNameSnapshot = ret.CustomerNameSnapshot,
            ReturnDate = ret.ReturnDate,
            Reason = ret.Reason,
            TotalAmount = ret.TotalAmount,
            Status = ret.Status,
            PostedAtUtc = ret.PostedAtUtc,
            CancelledAtUtc = ret.CancelledAtUtc,
            CancellationReason = ret.CancellationReason,
            Items = ret.Items.Select(i => new SaleReturnItemDto
            {
                Id = i.Id,
                SaleReturnId = i.SaleReturnId,
                OriginalSaleInvoiceItemId = i.OriginalSaleInvoiceItemId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                RefundAmount = i.RefundAmount,
                BatchAllocations = i.BatchAllocations.Select(b => new SaleReturnBatchAllocationDto
                {
                    Id = b.Id,
                    SaleReturnItemId = b.SaleReturnItemId,
                    OriginalSaleInvoiceBatchAllocationId = b.OriginalSaleInvoiceBatchAllocationId,
                    ProductBatchId = b.ProductBatchId,
                    Quantity = b.Quantity,
                    RestoredInventoryCostRate = b.RestoredInventoryCostRate,
                    RestoredInventoryValue = b.RestoredInventoryValue
                }).ToList()
            }).ToList()
        };
    }

    private class BatchCandidate
    {
        public int Id { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateOnly ExpiryDate { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal InventoryValue { get; set; }
        public decimal AveragePurchaseCost { get; set; }
    }

    private class StockMutationOutput
    {
        public decimal OldQuantity { get; set; }
        public decimal NewQuantity { get; set; }
        public decimal OldInventoryValue { get; set; }
        public decimal NewInventoryValue { get; set; }
        public decimal PreSaleAverageCost { get; set; }
        public decimal PostSaleAverageCost { get; set; }
        public decimal ValueConsumed { get; set; }
    }
}
