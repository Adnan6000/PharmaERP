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

public class PurchaseTransactionWriter : IPurchaseTransactionWriter
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAccountingOperationalGate _accountingGate;
    private readonly IAccountingTransactionWriter _accountingTransactionWriter;
    private readonly ILogger<PurchaseTransactionWriter> _logger;

    public PurchaseTransactionWriter(
        IDbContextFactory<AppDbContext> contextFactory,
        IAccountingOperationalGate accountingGate,
        IAccountingTransactionWriter accountingTransactionWriter,
        ILogger<PurchaseTransactionWriter> logger)
    {
        _contextFactory = contextFactory;
        _accountingGate = accountingGate;
        _accountingTransactionWriter = accountingTransactionWriter;
        _logger = logger;
    }

    public async Task<PurchaseInvoiceDto> CreateAndPostPurchaseInvoiceAsync(
        string invoiceNumber,
        PurchaseInvoiceCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ValidationException("Invoice number is required.");
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ValidationException("Purchase invoice must contain at least one line item.");
        }

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var supplier = await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.SupplierId, cancellationToken)
            ?? throw new NotFoundException($"Supplier with ID {dto.SupplierId} was not found.");

        if (!supplier.IsActive)
        {
            throw new ValidationException($"Supplier '{supplier.Name}' is deactivated and cannot receive new invoices.");
        }

        string? normalizedSupplierInv = !string.IsNullOrWhiteSpace(dto.SupplierInvoiceNumber)
            ? dto.SupplierInvoiceNumber.Trim().ToUpperInvariant()
            : null;

        if (normalizedSupplierInv != null)
        {
            bool duplicateSupplierInv = await context.PurchaseInvoices
                .AsNoTracking()
                .AnyAsync(i => i.SupplierId == dto.SupplierId &&
                               i.NormalizedSupplierInvoiceNumber == normalizedSupplierInv &&
                               i.Status != PurchaseInvoiceStatus.Cancelled,
                               cancellationToken);

            if (duplicateSupplierInv)
            {
                throw new DuplicateKeyException($"Supplier invoice number '{dto.SupplierInvoiceNumber}' already exists for this supplier.");
            }
        }

        // Validate items
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new ValidationException("Quantity must be greater than zero for all items.");
            }
            if (item.PurchaseRate < 0)
            {
                throw new ValidationException("Purchase rate cannot be negative.");
            }
            if (string.IsNullOrWhiteSpace(item.BatchNumber))
            {
                throw new ValidationException("Batch number is required for all items.");
            }
        }

        // Sort items deterministically by ProductId, NormalizedBatchNumber, ExpiryDate to prevent deadlocks
        var orderedItems = dto.Items
            .Select((item, index) => new
            {
                Item = item,
                NormalizedBatch = item.BatchNumber.Trim().ToUpperInvariant(),
                OriginalIndex = index
            })
            .OrderBy(x => x.Item.ProductId)
            .ThenBy(x => x.NormalizedBatch)
            .ThenBy(x => x.Item.ExpiryDate)
            .ToList();

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var nowUtc = DateTime.UtcNow;

            var invoice = new PurchaseInvoice
            {
                InvoiceNumber = invoiceNumber,
                SupplierId = dto.SupplierId,
                SupplierInvoiceNumber = dto.SupplierInvoiceNumber?.Trim(),
                NormalizedSupplierInvoiceNumber = normalizedSupplierInv,
                InvoiceDate = dto.InvoiceDate,
                DueDate = dto.DueDate,
                Remarks = dto.Remarks?.Trim(),
                Status = PurchaseInvoiceStatus.Posted,
                PostedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc
            };

            await context.PurchaseInvoices.AddAsync(invoice, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            decimal grossTotal = 0m;
            decimal totalDiscount = 0m;

            var connection = (SqlConnection)context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

            var invoiceItemEntities = new List<PurchaseInvoiceItem>();
            var itemDtos = new List<PurchaseInvoiceItemDto>();

            for (int i = 0; i < orderedItems.Count; i++)
            {
                var entry = orderedItems[i];
                var item = entry.Item;
                var rawBatch = item.BatchNumber.Trim();
                var normalizedBatch = entry.NormalizedBatch;

                var product = await context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                    ?? throw new NotFoundException($"Product with ID {item.ProductId} was not found.");

                // Race-safe find-or-create for ProductBatch
                var batch = await context.ProductBatches
                    .FirstOrDefaultAsync(b =>
                        b.ProductId == item.ProductId &&
                        b.NormalizedBatchNumber == normalizedBatch &&
                        b.ExpiryDate == item.ExpiryDate,
                        cancellationToken);

                if (batch == null)
                {
                    string savepoint = $"Batch_Create_{i}";
                    await transaction.CreateSavepointAsync(savepoint, cancellationToken);
                    try
                    {
                        batch = new ProductBatch
                        {
                            ProductId = item.ProductId,
                            BatchNumber = rawBatch,
                            NormalizedBatchNumber = normalizedBatch,
                            ExpiryDate = item.ExpiryDate,
                            ManufacturingDate = item.ManufacturingDate,
                            QuantityOnHand = 0m,
                            InventoryValue = 0m,
                            AveragePurchaseCost = item.PurchaseRate,
                            LastPurchaseCost = item.PurchaseRate,
                            SuggestedSalePrice = item.SuggestedSaleRate,
                            IsActive = true,
                            CreatedAtUtc = nowUtc
                        };

                        await context.ProductBatches.AddAsync(batch, cancellationToken);
                        await context.SaveChangesAsync(cancellationToken);
                        await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                    }
                    catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex))
                    {
                        await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                        context.Entry(batch!).State = EntityState.Detached;

                        batch = await context.ProductBatches
                            .FirstAsync(b =>
                                b.ProductId == item.ProductId &&
                                b.NormalizedBatchNumber == normalizedBatch &&
                                b.ExpiryDate == item.ExpiryDate,
                                cancellationToken);
                    }
                }

                // Atomic SQL mutation with OUTPUT
                decimal balanceAfter;
                decimal inventoryValueAfter;
                decimal avgCostAfter;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = dbTransaction;
                    cmd.CommandText = @"
                        UPDATE ProductBatches
                        SET QuantityOnHand = QuantityOnHand + @qty,
                            InventoryValue = InventoryValue + (@qty * @unitCost),
                            AveragePurchaseCost = CASE 
                                WHEN (QuantityOnHand + @qty) > 0 THEN ROUND((InventoryValue + (@qty * @unitCost)) / (QuantityOnHand + @qty), 4)
                                ELSE AveragePurchaseCost 
                            END,
                            LastPurchaseCost = @unitCost,
                            SuggestedSalePrice = COALESCE(@suggestedSalePrice, SuggestedSalePrice),
                            UpdatedAtUtc = @now
                        OUTPUT INSERTED.QuantityOnHand, INSERTED.InventoryValue, INSERTED.AveragePurchaseCost
                        WHERE Id = @batchId;";

                    cmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    cmd.Parameters.Add(new SqlParameter("@unitCost", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.PurchaseRate });
                    cmd.Parameters.Add(new SqlParameter("@suggestedSalePrice", SqlDbType.Decimal)
                    {
                        Precision = 18,
                        Scale = 4,
                        Value = (object?)item.SuggestedSaleRate ?? DBNull.Value
                    });
                    cmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    cmd.Parameters.Add(new SqlParameter("@batchId", SqlDbType.Int) { Value = batch.Id });

                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        throw new InvalidOperationException($"Failed to atomically increment stock for batch ID {batch.Id}.");
                    }

                    balanceAfter = reader.GetDecimal(0);
                    inventoryValueAfter = reader.GetDecimal(1);
                    avgCostAfter = reader.GetDecimal(2);
                }

                decimal itemGross = item.Quantity * item.PurchaseRate;
                decimal itemDiscount = item.DiscountAmount;
                decimal itemLineTotal = itemGross - itemDiscount;

                grossTotal += itemGross;
                totalDiscount += itemDiscount;

                var invoiceItem = new PurchaseInvoiceItem
                {
                    PurchaseInvoiceId = invoice.Id,
                    ProductId = item.ProductId,
                    ProductBatchId = batch.Id,
                    BatchNumber = rawBatch,
                    ExpiryDate = item.ExpiryDate,
                    ManufacturingDate = item.ManufacturingDate,
                    Quantity = item.Quantity,
                    PurchaseRate = item.PurchaseRate,
                    SuggestedSaleRate = item.SuggestedSaleRate,
                    DiscountAmount = itemDiscount,
                    LineTotal = itemLineTotal,
                    ReturnedQuantity = 0m,
                    CreatedAtUtc = nowUtc
                };

                invoiceItemEntities.Add(invoiceItem);
                await context.PurchaseInvoiceItems.AddAsync(invoiceItem, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                // Record StockMovement
                var movement = new StockMovement
                {
                    ProductId = item.ProductId,
                    ProductBatchId = batch.Id,
                    MovementType = StockMovementType.Purchase,
                    QuantityDelta = item.Quantity,
                    BalanceAfter = balanceAfter,
                    UnitCost = item.PurchaseRate,
                    InventoryCostRate = item.PurchaseRate,
                    InventoryValueDelta = item.Quantity * item.PurchaseRate,
                    InventoryValueAfter = inventoryValueAfter,
                    ReferenceDocumentType = StockReferenceDocumentType.PurchaseInvoice,
                    ReferenceDocumentId = invoice.Id,
                    ReferenceDocumentItemId = invoiceItem.Id,
                    ReferenceDocumentNumber = invoiceNumber,
                    TransactionDateUtc = nowUtc,
                    Remarks = $"Purchase invoice {invoiceNumber}"
                };

                await context.StockMovements.AddAsync(movement, cancellationToken);

                itemDtos.Add(new PurchaseInvoiceItemDto(
                    invoiceItem.Id,
                    invoice.Id,
                    product.Id,
                    product.Name,
                    batch.Id,
                    rawBatch,
                    item.ExpiryDate,
                    item.ManufacturingDate,
                    item.Quantity,
                    item.PurchaseRate,
                    item.SuggestedSaleRate,
                    itemDiscount,
                    itemLineTotal,
                    0m,
                    item.Quantity));
            }

            // Update invoice totals
            invoice.GrossTotal = grossTotal;
            invoice.DiscountAmount = totalDiscount;
            invoice.TaxAmount = 0m;
            invoice.NetTotal = grossTotal - totalDiscount;

            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostPurchaseInvoiceJournalAsync(context, invoice, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new PurchaseInvoiceDto(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.SupplierInvoiceNumber,
                supplier.Id,
                supplier.Name,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.Remarks,
                invoice.GrossTotal,
                invoice.DiscountAmount,
                invoice.TaxAmount,
                invoice.NetTotal,
                invoice.Status,
                invoice.PostedAtUtc,
                null,
                null,
                itemDtos,
                invoice.RowVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create and post purchase invoice '{InvoiceNumber}'.", invoiceNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CancelPurchaseInvoiceAsync(
        int purchaseInvoiceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("Cancellation reason is required.");
        }

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var invoice = await context.PurchaseInvoices
            .Include(i => i.Items)
                .ThenInclude(item => item.Product)
            .Include(i => i.Items)
                .ThenInclude(item => item.ProductBatch)
            .FirstOrDefaultAsync(i => i.Id == purchaseInvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Purchase invoice with ID {purchaseInvoiceId} was not found.");

        if (invoice.Status == PurchaseInvoiceStatus.Cancelled)
        {
            throw new ValidationException($"Purchase invoice '{invoice.InvoiceNumber}' is already cancelled.");
        }

        // Return Lifecycle Guard:
        // 1. Verify every PurchaseInvoiceItem.ReturnedQuantity == 0.
        // 2. Verify there is no linked PurchaseReturn for this invoice whose status is Posted / otherwise non-cancelled.
        const string returnRejectMessage = "This purchase has supplier returns recorded against it. Cancel those purchase returns first, then try cancelling the purchase again.";

        if (invoice.Items.Any(item => item.ReturnedQuantity > 0))
        {
            throw new ValidationException(returnRejectMessage);
        }

        bool hasActiveLinkedReturns = await context.PurchaseReturns
            .AsNoTracking()
            .AnyAsync(r => r.OriginalPurchaseInvoiceId == purchaseInvoiceId &&
                           r.Status != PurchaseReturnStatus.Cancelled,
                           cancellationToken);

        if (hasActiveLinkedReturns)
        {
            throw new ValidationException(returnRejectMessage);
        }

        // Guard: Verify no active payment voucher settlement allocations exist for this invoice
        bool hasActiveAllocations = await context.PaymentVoucherAllocations
            .AnyAsync(a => a.PurchaseInvoiceId == purchaseInvoiceId && a.Status == AllocationStatus.Active, cancellationToken);

        if (hasActiveAllocations)
        {
            throw new ValidationException(
                $"Cannot cancel purchase invoice '{invoice.InvoiceNumber}' because it has active payment voucher settlement allocations. Remove or void allocations first.");
        }


        // Conservative verification: check downstream outflows on any of the batches
        var invoiceDate = invoice.PostedAtUtc ?? invoice.CreatedAtUtc;

        foreach (var item in invoice.Items)
        {
            if (item.ProductBatch.QuantityOnHand < item.Quantity)
            {
                throw new ValidationException(
                    $"Cannot cancel purchase invoice because stock in batch '{item.BatchNumber}' is insufficient " +
                    $"({item.ProductBatch.QuantityOnHand} available, {item.Quantity} required to reverse).");
            }

            bool hasOutflows = await context.StockMovements
                .AsNoTracking()
                .AnyAsync(m =>
                    m.ProductBatchId == item.ProductBatchId &&
                    m.TransactionDateUtc >= invoiceDate &&
                    m.QuantityDelta < 0 &&
                    !context.StockMovements.Any(rev => rev.ReversesStockMovementId == m.Id),
                    cancellationToken);

            if (hasOutflows)
            {
                throw new ValidationException(
                    $"Cannot cancel purchase invoice because stock from batch '{item.BatchNumber}' has already been consumed by subsequent sales or returns.");
            }
        }

        // Sort items deterministically
        var sortedItems = invoice.Items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.ProductBatch.NormalizedBatchNumber)
            .ThenBy(i => i.ExpiryDate)
            .ToList();

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var nowUtc = DateTime.UtcNow;
            var connection = (SqlConnection)context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

            foreach (var item in sortedItems)
            {
                decimal balanceAfter;
                decimal inventoryValueAfter;
                decimal avgCostAfter;
                decimal originalLineValue = item.Quantity * item.PurchaseRate;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = dbTransaction;
                    cmd.CommandText = @"
                        UPDATE ProductBatches
                        SET QuantityOnHand = QuantityOnHand - @qty,
                            InventoryValue = CASE 
                                WHEN (QuantityOnHand - @qty) <= 0 THEN 0
                                WHEN (InventoryValue - @valDelta) < 0 THEN 0
                                ELSE InventoryValue - @valDelta
                            END,
                            AveragePurchaseCost = CASE 
                                WHEN (QuantityOnHand - @qty) <= 0 THEN 0
                                ELSE CASE 
                                    WHEN (InventoryValue - @valDelta) <= 0 THEN 0
                                    ELSE ROUND((InventoryValue - @valDelta) / (QuantityOnHand - @qty), 4)
                                END
                            END,
                            UpdatedAtUtc = @now
                        OUTPUT INSERTED.QuantityOnHand, INSERTED.InventoryValue, INSERTED.AveragePurchaseCost
                        WHERE Id = @batchId AND QuantityOnHand >= @qty;";

                    cmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    cmd.Parameters.Add(new SqlParameter("@valDelta", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = originalLineValue });
                    cmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    cmd.Parameters.Add(new SqlParameter("@batchId", SqlDbType.Int) { Value = item.ProductBatchId });

                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        throw new ValidationException(
                            $"Insufficient stock in batch '{item.BatchNumber}' to reverse purchase invoice.");
                    }

                    balanceAfter = reader.GetDecimal(0);
                    inventoryValueAfter = reader.GetDecimal(1);
                    avgCostAfter = reader.GetDecimal(2);
                }

                // Find original Purchase StockMovement to link ReversesStockMovementId
                var origMovement = await context.StockMovements
                    .FirstOrDefaultAsync(m =>
                        m.MovementType == StockMovementType.Purchase &&
                        m.ReferenceDocumentType == StockReferenceDocumentType.PurchaseInvoice &&
                        m.ReferenceDocumentId == invoice.Id &&
                        m.ReferenceDocumentItemId == item.Id,
                        cancellationToken);

                // Record compensating StockMovement
                var movement = new StockMovement
                {
                    ProductId = item.ProductId,
                    ProductBatchId = item.ProductBatchId,
                    MovementType = StockMovementType.PurchaseReversal,
                    QuantityDelta = -item.Quantity,
                    BalanceAfter = balanceAfter,
                    UnitCost = item.PurchaseRate,
                    InventoryCostRate = item.PurchaseRate,
                    InventoryValueDelta = -originalLineValue,
                    InventoryValueAfter = inventoryValueAfter,
                    ReferenceDocumentType = StockReferenceDocumentType.PurchaseInvoice,
                    ReferenceDocumentId = invoice.Id,
                    ReferenceDocumentItemId = item.Id,
                    ReferenceDocumentNumber = invoice.InvoiceNumber,
                    ReversesStockMovementId = origMovement?.Id,
                    TransactionDateUtc = nowUtc,
                    Remarks = $"Cancellation reversal: {reason.Trim()}"
                };

                await context.StockMovements.AddAsync(movement, cancellationToken);
            }

            invoice.Status = PurchaseInvoiceStatus.Cancelled;
            invoice.CancelledAtUtc = nowUtc;
            invoice.CancellationReason = reason.Trim();
            invoice.UpdatedAtUtc = nowUtc;

            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostPurchaseInvoiceReversalAsync(context, invoice, reason.Trim(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel purchase invoice ID {InvoiceId}.", purchaseInvoiceId);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PurchaseReturnDto> CreateAndPostPurchaseReturnAsync(
        string returnNumber,
        PurchaseReturnCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(returnNumber))
        {
            throw new ValidationException("Return number is required.");
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ValidationException("Purchase return must contain at least one item.");
        }

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var supplier = await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.SupplierId, cancellationToken)
            ?? throw new NotFoundException($"Supplier with ID {dto.SupplierId} was not found.");

        PurchaseInvoice? originalInvoice = null;
        if (dto.OriginalPurchaseInvoiceId.HasValue)
        {
            originalInvoice = await context.PurchaseInvoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == dto.OriginalPurchaseInvoiceId.Value, cancellationToken)
                ?? throw new NotFoundException($"Original purchase invoice with ID {dto.OriginalPurchaseInvoiceId.Value} was not found.");

            if (originalInvoice.SupplierId != dto.SupplierId)
            {
                throw new ValidationException("Original purchase invoice does not belong to the specified supplier.");
            }

            if (originalInvoice.Status != PurchaseInvoiceStatus.Posted)
            {
                throw new ValidationException("Can only create returns against posted purchase invoices.");
            }
        }

        // Validate items
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new ValidationException("Return quantity must be greater than zero.");
            }

            if (item.OriginalPurchaseInvoiceItemId.HasValue)
            {
                if (originalInvoice == null)
                {
                    throw new ValidationException("Return item references an invoice item, but no original invoice is linked.");
                }

                var origLine = originalInvoice.Items.FirstOrDefault(l => l.Id == item.OriginalPurchaseInvoiceItemId.Value)
                    ?? throw new NotFoundException($"Original invoice line with ID {item.OriginalPurchaseInvoiceItemId.Value} was not found.");

                decimal remaining = origLine.Quantity - origLine.ReturnedQuantity;
                if (item.Quantity > remaining)
                {
                    throw new ValidationException(
                        $"Return quantity {item.Quantity} exceeds remaining returnable quantity {remaining} on original invoice line.");
                }
            }
            else
            {
                if (!item.ReturnRate.HasValue || item.ReturnRate.Value < 0)
                {
                    throw new ValidationException("Return rate is required and cannot be negative for unlinked returns.");
                }
            }
        }

        // Sort items deterministically
        var sortedItems = dto.Items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.ProductBatchId)
            .ToList();

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var nowUtc = DateTime.UtcNow;

            var purchaseReturn = new PurchaseReturn
            {
                ReturnNumber = returnNumber,
                SupplierId = dto.SupplierId,
                OriginalPurchaseInvoiceId = dto.OriginalPurchaseInvoiceId,
                ReturnDate = dto.ReturnDate,
                Reason = dto.Reason?.Trim(),
                Status = PurchaseReturnStatus.Posted,
                PostedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc
            };

            await context.PurchaseReturns.AddAsync(purchaseReturn, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            decimal totalAmount = 0m;

            var connection = (SqlConnection)context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

            var returnItemDtos = new List<PurchaseReturnItemDto>();

            foreach (var item in sortedItems)
            {
                var product = await context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                    ?? throw new NotFoundException($"Product with ID {item.ProductId} was not found.");

                var batch = await context.ProductBatches
                    .FirstOrDefaultAsync(b => b.Id == item.ProductBatchId, cancellationToken)
                    ?? throw new NotFoundException($"Batch with ID {item.ProductBatchId} was not found.");

                decimal effectiveRate;

                if (item.OriginalPurchaseInvoiceItemId.HasValue)
                {
                    var origLine = await context.PurchaseInvoiceItems
                        .FirstOrDefaultAsync(l => l.Id == item.OriginalPurchaseInvoiceItemId.Value, cancellationToken)
                        ?? throw new NotFoundException($"Invoice line {item.OriginalPurchaseInvoiceItemId.Value} not found.");

                    effectiveRate = origLine.PurchaseRate;

                    // Atomic cumulative return check at DB level
                    using var guardCmd = connection.CreateCommand();
                    guardCmd.Transaction = dbTransaction;
                    guardCmd.CommandText = @"
                        UPDATE PurchaseInvoiceItems
                        SET ReturnedQuantity = ReturnedQuantity + @qty,
                            UpdatedAtUtc = @now
                        WHERE Id = @id AND (ReturnedQuantity + @qty) <= Quantity;";

                    guardCmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    guardCmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    guardCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = item.OriginalPurchaseInvoiceItemId.Value });

                    int rowsAffected = await guardCmd.ExecuteNonQueryAsync(cancellationToken);
                    if (rowsAffected == 0)
                    {
                        throw new ValidationException(
                            $"Atomic return guard failed: Returned quantity {item.Quantity} would exceed original invoice item quantity.");
                    }
                }
                else
                {
                    effectiveRate = item.ReturnRate!.Value;
                }

                // Atomic stock decrement with OUTPUT
                decimal balanceAfter;
                decimal inventoryValueAfter;
                decimal avgCostAfter;
                decimal returnValDelta = item.Quantity * effectiveRate;

                using (var stockCmd = connection.CreateCommand())
                {
                    stockCmd.Transaction = dbTransaction;
                    stockCmd.CommandText = @"
                        UPDATE ProductBatches
                        SET QuantityOnHand = QuantityOnHand - @qty,
                            InventoryValue = CASE 
                                WHEN (QuantityOnHand - @qty) <= 0 THEN 0
                                WHEN (InventoryValue - @valDelta) < 0 THEN 0
                                ELSE InventoryValue - @valDelta
                            END,
                            AveragePurchaseCost = CASE 
                                WHEN (QuantityOnHand - @qty) <= 0 THEN 0
                                ELSE CASE 
                                    WHEN (InventoryValue - @valDelta) <= 0 THEN 0
                                    ELSE ROUND((InventoryValue - @valDelta) / (QuantityOnHand - @qty), 4)
                                END
                            END,
                            UpdatedAtUtc = @now
                        OUTPUT INSERTED.QuantityOnHand, INSERTED.InventoryValue, INSERTED.AveragePurchaseCost
                        WHERE Id = @batchId AND QuantityOnHand >= @qty;";

                    stockCmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    stockCmd.Parameters.Add(new SqlParameter("@valDelta", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = returnValDelta });
                    stockCmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    stockCmd.Parameters.Add(new SqlParameter("@batchId", SqlDbType.Int) { Value = item.ProductBatchId });

                    using var reader = await stockCmd.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        throw new ValidationException(
                            $"Insufficient stock in batch '{batch.BatchNumber}' to process return. Available: {batch.QuantityOnHand}, Requested: {item.Quantity}.");
                    }

                    balanceAfter = reader.GetDecimal(0);
                    inventoryValueAfter = reader.GetDecimal(1);
                    avgCostAfter = reader.GetDecimal(2);
                }

                decimal lineTotal = item.Quantity * effectiveRate;
                totalAmount += lineTotal;

                var returnItem = new PurchaseReturnItem
                {
                    PurchaseReturnId = purchaseReturn.Id,
                    OriginalPurchaseInvoiceItemId = item.OriginalPurchaseInvoiceItemId,
                    ProductId = item.ProductId,
                    ProductBatchId = item.ProductBatchId,
                    Quantity = item.Quantity,
                    ReturnRate = effectiveRate,
                    LineTotal = lineTotal,
                    CreatedAtUtc = nowUtc
                };

                await context.PurchaseReturnItems.AddAsync(returnItem, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                // Record StockMovement
                var movement = new StockMovement
                {
                    ProductId = item.ProductId,
                    ProductBatchId = item.ProductBatchId,
                    MovementType = StockMovementType.PurchaseReturn,
                    QuantityDelta = -item.Quantity,
                    BalanceAfter = balanceAfter,
                    UnitCost = effectiveRate,
                    InventoryCostRate = effectiveRate,
                    InventoryValueDelta = -returnValDelta,
                    InventoryValueAfter = inventoryValueAfter,
                    ReferenceDocumentType = StockReferenceDocumentType.PurchaseReturn,
                    ReferenceDocumentId = purchaseReturn.Id,
                    ReferenceDocumentItemId = returnItem.Id,
                    ReferenceDocumentNumber = returnNumber,
                    TransactionDateUtc = nowUtc,
                    Remarks = $"Purchase return {returnNumber}"
                };

                await context.StockMovements.AddAsync(movement, cancellationToken);

                returnItemDtos.Add(new PurchaseReturnItemDto(
                    returnItem.Id,
                    purchaseReturn.Id,
                    item.OriginalPurchaseInvoiceItemId,
                    product.Id,
                    product.Name,
                    batch.Id,
                    batch.BatchNumber,
                    item.Quantity,
                    effectiveRate,
                    lineTotal));
            }

            purchaseReturn.TotalAmount = totalAmount;
            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostPurchaseReturnJournalAsync(context, purchaseReturn, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new PurchaseReturnDto(
                purchaseReturn.Id,
                purchaseReturn.ReturnNumber,
                originalInvoice?.Id,
                originalInvoice?.InvoiceNumber,
                supplier.Id,
                supplier.Name,
                purchaseReturn.ReturnDate,
                purchaseReturn.Reason,
                purchaseReturn.TotalAmount,
                purchaseReturn.Status,
                purchaseReturn.PostedAtUtc,
                returnItemDtos,
                purchaseReturn.RowVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create and post purchase return '{ReturnNumber}'.", returnNumber);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CancelPurchaseReturnAsync(
        int purchaseReturnId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("Cancellation reason is required.");
        }

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var purchaseReturn = await context.PurchaseReturns
            .Include(r => r.Items)
                .ThenInclude(item => item.Product)
            .Include(r => r.Items)
                .ThenInclude(item => item.ProductBatch)
            .FirstOrDefaultAsync(r => r.Id == purchaseReturnId, cancellationToken)
            ?? throw new NotFoundException($"Purchase return with ID {purchaseReturnId} was not found.");

        if (purchaseReturn.Status == PurchaseReturnStatus.Cancelled)
        {
            throw new ValidationException($"Purchase return '{purchaseReturn.ReturnNumber}' is already cancelled.");
        }

        if (purchaseReturn.Status != PurchaseReturnStatus.Posted)
        {
            throw new ValidationException("Only posted purchase returns can be cancelled.");
        }

        var sortedItems = purchaseReturn.Items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.ProductBatchId)
            .ToList();

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var nowUtc = DateTime.UtcNow;
            var connection = (SqlConnection)context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

            foreach (var item in sortedItems)
            {
                decimal restoredValue = item.Quantity * item.ReturnRate;

                // 1. Revert ReturnedQuantity on OriginalPurchaseInvoiceItem if linked
                if (item.OriginalPurchaseInvoiceItemId.HasValue)
                {
                    using var guardCmd = connection.CreateCommand();
                    guardCmd.Transaction = dbTransaction;
                    guardCmd.CommandText = @"
                        UPDATE PurchaseInvoiceItems
                        SET ReturnedQuantity = ReturnedQuantity - @qty,
                            UpdatedAtUtc = @now
                        WHERE Id = @id AND (ReturnedQuantity - @qty) >= 0;";

                    guardCmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    guardCmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    guardCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = item.OriginalPurchaseInvoiceItemId.Value });

                    int rowsAffected = await guardCmd.ExecuteNonQueryAsync(cancellationToken);
                    if (rowsAffected == 0)
                    {
                        throw new ValidationException(
                            $"Failed to revert returned quantity on invoice line {item.OriginalPurchaseInvoiceItemId.Value}: invalid cumulative quantity.");
                    }
                }

                // 2. Restore stock and value in ProductBatches
                decimal balanceAfter;
                decimal inventoryValueAfter;
                decimal avgCostAfter;

                using (var stockCmd = connection.CreateCommand())
                {
                    stockCmd.Transaction = dbTransaction;
                    stockCmd.CommandText = @"
                        UPDATE ProductBatches
                        SET QuantityOnHand = QuantityOnHand + @qty,
                            InventoryValue = InventoryValue + @valDelta,
                            AveragePurchaseCost = CASE 
                                WHEN (QuantityOnHand + @qty) > 0 THEN ROUND((InventoryValue + @valDelta) / (QuantityOnHand + @qty), 4)
                                ELSE AveragePurchaseCost
                            END,
                            UpdatedAtUtc = @now
                        OUTPUT INSERTED.QuantityOnHand, INSERTED.InventoryValue, INSERTED.AveragePurchaseCost
                        WHERE Id = @batchId;";

                    stockCmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = item.Quantity });
                    stockCmd.Parameters.Add(new SqlParameter("@valDelta", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = restoredValue });
                    stockCmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
                    stockCmd.Parameters.Add(new SqlParameter("@batchId", SqlDbType.Int) { Value = item.ProductBatchId });

                    using var reader = await stockCmd.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        throw new InvalidOperationException($"Failed to restore stock for batch ID {item.ProductBatchId}.");
                    }

                    balanceAfter = reader.GetDecimal(0);
                    inventoryValueAfter = reader.GetDecimal(1);
                    avgCostAfter = reader.GetDecimal(2);
                }

                // 3. Find original PurchaseReturn StockMovement to link ReversesStockMovementId
                var origMovement = await context.StockMovements
                    .FirstOrDefaultAsync(m =>
                        m.MovementType == StockMovementType.PurchaseReturn &&
                        m.ReferenceDocumentType == StockReferenceDocumentType.PurchaseReturn &&
                        m.ReferenceDocumentId == purchaseReturn.Id &&
                        m.ReferenceDocumentItemId == item.Id,
                        cancellationToken);

                // 4. Create compensating PurchaseReturnReversal StockMovement
                var movement = new StockMovement
                {
                    ProductId = item.ProductId,
                    ProductBatchId = item.ProductBatchId,
                    MovementType = StockMovementType.PurchaseReturnReversal,
                    QuantityDelta = item.Quantity,
                    BalanceAfter = balanceAfter,
                    UnitCost = item.ReturnRate,
                    InventoryCostRate = item.ReturnRate,
                    InventoryValueDelta = restoredValue,
                    InventoryValueAfter = inventoryValueAfter,
                    ReferenceDocumentType = StockReferenceDocumentType.PurchaseReturn,
                    ReferenceDocumentId = purchaseReturn.Id,
                    ReferenceDocumentItemId = item.Id,
                    ReferenceDocumentNumber = purchaseReturn.ReturnNumber,
                    ReversesStockMovementId = origMovement?.Id,
                    TransactionDateUtc = nowUtc,
                    Remarks = $"Purchase return cancellation reversal: {reason.Trim()}"
                };

                await context.StockMovements.AddAsync(movement, cancellationToken);
            }

            purchaseReturn.Status = PurchaseReturnStatus.Cancelled;
            purchaseReturn.CancelledAtUtc = nowUtc;
            purchaseReturn.Reason = string.IsNullOrWhiteSpace(purchaseReturn.Reason)
                ? $"Cancelled: {reason.Trim()}"
                : $"{purchaseReturn.Reason} | Cancelled: {reason.Trim()}";
            purchaseReturn.UpdatedAtUtc = nowUtc;

            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostPurchaseReturnReversalAsync(context, purchaseReturn, reason.Trim(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel purchase return ID {ReturnId}.", purchaseReturnId);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

