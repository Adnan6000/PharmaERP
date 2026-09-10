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
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class InventoryTransactionWriter : IInventoryTransactionWriter
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAccountingOperationalGate _accountingGate;
    private readonly IAccountingTransactionWriter _accountingTransactionWriter;
    private readonly ILogger<InventoryTransactionWriter> _logger;

    public InventoryTransactionWriter(
        IDbContextFactory<AppDbContext> contextFactory,
        IAccountingOperationalGate accountingGate,
        IAccountingTransactionWriter accountingTransactionWriter,
        ILogger<InventoryTransactionWriter> logger)
    {
        _contextFactory = contextFactory;
        _accountingGate = accountingGate;
        _accountingTransactionWriter = accountingTransactionWriter;
        _logger = logger;
    }

    public async Task<ProductBatchDto> RecordOpeningStockAsync(
        OpeningStockCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Quantity <= 0)
        {
            throw new ValidationException("Opening stock quantity must be greater than zero.");
        }

        if (dto.UnitCost < 0)
        {
            throw new ValidationException("Unit cost cannot be negative.");
        }

        string rawBatch = dto.BatchNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawBatch))
        {
            throw new ValidationException("Batch number is required.");
        }

        string normalizedBatch = rawBatch.ToUpperInvariant();

        await using var gate = await _accountingGate.AcquireSharedOperationalGateAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dto.ProductId, cancellationToken)
            ?? throw new NotFoundException($"Product with ID {dto.ProductId} was not found.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var batch = await context.ProductBatches
                .FirstOrDefaultAsync(b =>
                    b.ProductId == dto.ProductId &&
                    b.NormalizedBatchNumber == normalizedBatch &&
                    b.ExpiryDate == dto.ExpiryDate,
                    cancellationToken);

            if (batch == null)
            {
                await transaction.CreateSavepointAsync("BatchCreate", cancellationToken);
                try
                {
                    batch = new ProductBatch
                    {
                        ProductId = dto.ProductId,
                        BatchNumber = rawBatch,
                        NormalizedBatchNumber = normalizedBatch,
                        ExpiryDate = dto.ExpiryDate,
                        ManufacturingDate = dto.ManufacturingDate,
                        QuantityOnHand = 0m,
                        InventoryValue = 0m,
                        AveragePurchaseCost = dto.UnitCost,
                        LastPurchaseCost = dto.UnitCost,
                        SuggestedSalePrice = dto.SuggestedSalePrice,
                        IsActive = true
                    };

                    await context.ProductBatches.AddAsync(batch, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.ReleaseSavepointAsync("BatchCreate", cancellationToken);
                }
                catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex))
                {
                    await transaction.RollbackToSavepointAsync("BatchCreate", cancellationToken);
                    context.Entry(batch!).State = EntityState.Detached;

                    batch = await context.ProductBatches
                        .FirstAsync(b =>
                            b.ProductId == dto.ProductId &&
                            b.NormalizedBatchNumber == normalizedBatch &&
                            b.ExpiryDate == dto.ExpiryDate,
                            cancellationToken);
                }
            }

            // Atomic SQL update with OUTPUT
            var connection = (SqlConnection)context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var dbTransaction = (SqlTransaction)transaction.GetDbTransaction();

            decimal balanceAfter;
            decimal inventoryValueAfter;
            decimal avgCostAfter;
            byte[] rowVersion;

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
                    OUTPUT INSERTED.QuantityOnHand, INSERTED.InventoryValue, INSERTED.AveragePurchaseCost, INSERTED.RowVersion
                    WHERE Id = @batchId;";

                cmd.Parameters.Add(new SqlParameter("@qty", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = dto.Quantity });
                cmd.Parameters.Add(new SqlParameter("@unitCost", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = dto.UnitCost });
                cmd.Parameters.Add(new SqlParameter("@suggestedSalePrice", SqlDbType.Decimal)
                {
                    Precision = 18,
                    Scale = 4,
                    Value = (object?)dto.SuggestedSalePrice ?? DBNull.Value
                });
                cmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
                cmd.Parameters.Add(new SqlParameter("@batchId", SqlDbType.Int) { Value = batch.Id });

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"Failed to atomically update stock for batch ID {batch.Id}.");
                }

                balanceAfter = reader.GetDecimal(0);
                inventoryValueAfter = reader.GetDecimal(1);
                avgCostAfter = reader.GetDecimal(2);
                rowVersion = (byte[])reader.GetValue(3);
            }

            // Record StockMovement ledger entry
            var movement = new StockMovement
            {
                ProductId = dto.ProductId,
                ProductBatchId = batch.Id,
                MovementType = StockMovementType.OpeningStock,
                QuantityDelta = dto.Quantity,
                BalanceAfter = balanceAfter,
                UnitCost = dto.UnitCost,
                InventoryCostRate = dto.UnitCost,
                InventoryValueDelta = dto.Quantity * dto.UnitCost,
                InventoryValueAfter = inventoryValueAfter,
                ReferenceDocumentType = StockReferenceDocumentType.OpeningStock,
                ReferenceDocumentNumber = "OPENING",
                TransactionDateUtc = DateTime.UtcNow,
                Remarks = "Opening stock entry"
            };

            await context.StockMovements.AddAsync(movement, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            await _accountingTransactionWriter.PostOpeningStockJournalAsync(context, movement, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            int daysUntil = batch.ExpiryDate.DayNumber - today.DayNumber;

            return new ProductBatchDto(
                batch.Id,
                batch.ProductId,
                product.Name,
                batch.BatchNumber,
                batch.NormalizedBatchNumber,
                batch.ManufacturingDate,
                batch.ExpiryDate,
                balanceAfter,
                inventoryValueAfter,
                avgCostAfter,
                dto.UnitCost,
                dto.SuggestedSalePrice ?? batch.SuggestedSalePrice,
                batch.IsActive,
                daysUntil < 0,
                daysUntil >= 0 && daysUntil <= 90,
                daysUntil,
                rowVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record opening stock for Product ID {ProductId}, Batch '{Batch}'.", dto.ProductId, rawBatch);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
