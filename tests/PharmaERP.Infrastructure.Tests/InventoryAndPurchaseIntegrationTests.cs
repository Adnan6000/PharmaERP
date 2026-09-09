using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;
using PharmaERP.Infrastructure.Persistence.Services;

namespace PharmaERP.Infrastructure.Tests;

[CollectionDefinition("SqlServerDatabaseCollection")]
public class SqlServerDatabaseCollection : ICollectionFixture<SqlServerTestFixture>
{
}

[Collection("SqlServerDatabaseCollection")]
public class InventoryAndPurchaseIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerTestFixture _fixture;

    public InventoryAndPurchaseIntegrationTests(SqlServerTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int ProductId, int SupplierId)> SeedProductAndSupplierAsync()
    {
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();

        var supplier = new Supplier
        {
            SupplierCode = "SUPP-001",
            Name = "GlaxoSmithKline Dist",
            IsActive = true
        };
        await context.Suppliers.AddAsync(supplier);

        var product = new Product
        {
            Name = "Augmentin 625mg",
            ProductCode = "AUG-625",
            DefaultPurchasePrice = 120.00m,
            DefaultSalePrice = 150.00m,
            IsActive = true
        };
        await context.Products.AddAsync(product);

        await context.SaveChangesAsync();
        return (product.Id, supplier.Id);
    }

    [Fact]
    public async Task DocumentNumberGenerator_ConcurrentRequests_ProducesUniqueSequentialNumbers()
    {
        var generator = new DocumentNumberGenerator(_fixture.ContextFactory, NullLogger<DocumentNumberGenerator>.Instance);

        const int taskCount = 10;
        var tasks = Enumerable.Range(0, taskCount)
            .Select(_ => generator.NextDocumentNumberAsync(DocumentType.PurchaseInvoice, 2026))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Every number must be distinct
        Assert.Equal(taskCount, results.Distinct().Count());
        Assert.All(results, num => Assert.StartsWith("PINV-2026-", num));
    }

    [Fact]
    public async Task OpeningStock_RecordsStockMovementAndUpdatesBatchQuantities()
    {
        var (productId, _) = await SeedProductAndSupplierAsync();

        var writer = new InventoryTransactionWriter(_fixture.ContextFactory, NullLogger<InventoryTransactionWriter>.Instance);

        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));
        var batchDto = await writer.RecordOpeningStockAsync(new OpeningStockCreateDto
        {
            ProductId = productId,
            BatchNumber = "BATCH-OP-1",
            ExpiryDate = exp,
            Quantity = 50m,
            UnitCost = 100m,
            SuggestedSalePrice = 130m
        });

        Assert.Equal(50m, batchDto.QuantityOnHand);
        Assert.Equal(5000m, batchDto.InventoryValue);
        Assert.Equal(100m, batchDto.AveragePurchaseCost);

        // Verify movement
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var movement = await context.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batchDto.Id);
        Assert.NotNull(movement);
        Assert.Equal(StockMovementType.OpeningStock, movement.MovementType);
        Assert.Equal(50m, movement.QuantityDelta);
        Assert.Equal(50m, movement.BalanceAfter);
    }

    [Fact]
    public async Task PurchaseInvoice_AtomicPost_CalculatesWeightedAverageCostAndIncrementsStock()
    {
        var (productId, supplierId) = await SeedProductAndSupplierAsync();

        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);

        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));
        var invoiceDto = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-2026-000001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-GSK-101",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-GSK-A",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 120m,
                    DiscountAmount = 200m
                }
            ]
        });

        Assert.Equal(PurchaseInvoiceStatus.Posted, invoiceDto.Status);
        Assert.Equal(12000m, invoiceDto.GrossTotal);
        Assert.Equal(200m, invoiceDto.DiscountAmount);
        Assert.Equal(11800m, invoiceDto.NetTotal);

        // Verify batch state
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.SingleAsync(b => b.ProductId == productId);
        Assert.Equal(100m, batch.QuantityOnHand);
        Assert.Equal(12000m, batch.InventoryValue);
        Assert.Equal(120m, batch.AveragePurchaseCost);

        // Second purchase with different rate should update moving weighted average
        await writer.CreateAndPostPurchaseInvoiceAsync("PINV-2026-000002", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-GSK-102",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-GSK-A",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 140m, // Higher rate
                    DiscountAmount = 0m
                }
            ]
        });

        await using var context2 = await _fixture.ContextFactory.CreateDbContextAsync();
        var batchUpdated = await context2.ProductBatches.SingleAsync(b => b.ProductId == productId);
        Assert.Equal(200m, batchUpdated.QuantityOnHand);
        Assert.Equal(26000m, batchUpdated.InventoryValue); // 12000 + 14000
        Assert.Equal(130m, batchUpdated.AveragePurchaseCost); // 26000 / 200 = 130
    }

    [Fact]
    public async Task ConcurrentNewBatchCreation_RaceSafeFindOrCreate_SucceedsWithoutLosingTransactions()
    {
        var (productId, supplierId) = await SeedProductAndSupplierAsync();

        var writer1 = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var writer2 = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);

        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var t1 = writer1.CreateAndPostPurchaseInvoiceAsync("PINV-CONC-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-C1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "RACE-BATCH-1",
                    ExpiryDate = exp,
                    Quantity = 10m,
                    PurchaseRate = 100m
                }
            ]
        });

        var t2 = writer2.CreateAndPostPurchaseInvoiceAsync("PINV-CONC-002", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-C2",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "RACE-BATCH-1",
                    ExpiryDate = exp,
                    Quantity = 20m,
                    PurchaseRate = 100m
                }
            ]
        });

        // Run concurrently
        await Task.WhenAll(t1, t2);

        // Verify that exactly ONE batch exists with 30 units on hand
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batches = await context.ProductBatches.Where(b => b.ProductId == productId && b.NormalizedBatchNumber == "RACE-BATCH-1").ToListAsync();
        Assert.Single(batches);
        Assert.Equal(30m, batches[0].QuantityOnHand);
    }

    [Fact]
    public async Task PurchaseReturn_AtomicCumulativeGuard_BlocksReturnExceedingRemaining()
    {
        var (productId, supplierId) = await SeedProductAndSupplierAsync();

        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);

        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));
        var invoice = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-RET-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-RET-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "RET-BATCH-1",
                    ExpiryDate = exp,
                    Quantity = 10m,
                    PurchaseRate = 50m
                }
            ]
        });

        var invoiceItemId = invoice.Items[0].Id;
        var batchId = invoice.Items[0].ProductBatchId;

        // Return 6 units (Valid)
        var returnDto = await writer.CreateAndPostPurchaseReturnAsync("PRET-2026-000001", new PurchaseReturnCreateDto
        {
            SupplierId = supplierId,
            OriginalPurchaseInvoiceId = invoice.Id,
            ReturnDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseReturnItemCreateDto
                {
                    OriginalPurchaseInvoiceItemId = invoiceItemId,
                    ProductId = productId,
                    ProductBatchId = batchId,
                    Quantity = 6m
                }
            ]
        });

        Assert.Equal(PurchaseReturnStatus.Posted, returnDto.Status);

        // Attempting to return 5 more units should fail (only 4 remain)
        await Assert.ThrowsAsync<ValidationException>(() => writer.CreateAndPostPurchaseReturnAsync("PRET-2026-000002", new PurchaseReturnCreateDto
        {
            SupplierId = supplierId,
            OriginalPurchaseInvoiceId = invoice.Id,
            ReturnDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseReturnItemCreateDto
                {
                    OriginalPurchaseInvoiceItemId = invoiceItemId,
                    ProductId = productId,
                    ProductBatchId = batchId,
                    Quantity = 5m // Exceeds remaining 4
                }
            ]
        }));
    }

    [Fact]
    public async Task TestA_PurchaseCancellation_AfterLaterReceipt_ReversesHistoricalValue_RecalculatesAverageAccurately()
    {
        // Scenario:
        // Purchase A: 100 @ 10
        // Purchase B: 100 @ 20
        // Current Qty = 200, InventoryValue = 3000, AveragePurchaseCost = 15
        // Cancelling Purchase A must leave: Qty = 100, InventoryValue = 2000, AveragePurchaseCost = 20
        var (productId, supplierId) = await SeedProductAndSupplierAsync();
        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var invoiceA = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-A-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-A",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-HIST-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 10m
                }
            ]
        });

        var invoiceB = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-B-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-B",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-HIST-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 20m
                }
            ]
        });

        // Pre-cancellation check
        await using (var preCtx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b = await preCtx.ProductBatches.SingleAsync(x => x.ProductId == productId && x.NormalizedBatchNumber == "BATCH-HIST-1");
            Assert.Equal(200m, b.QuantityOnHand);
            Assert.Equal(3000m, b.InventoryValue);
            Assert.Equal(15m, b.AveragePurchaseCost);
        }

        // Cancel Purchase A
        await writer.CancelPurchaseInvoiceAsync(invoiceA.Id, "Cancelling first purchase invoice");

        // Verify batch state post-cancellation
        await using (var postCtx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b = await postCtx.ProductBatches.SingleAsync(x => x.ProductId == productId && x.NormalizedBatchNumber == "BATCH-HIST-1");
            Assert.Equal(100m, b.QuantityOnHand);
            Assert.Equal(2000m, b.InventoryValue);
            Assert.Equal(20m, b.AveragePurchaseCost);

            // Verify StockMovement for PurchaseReversal
            var reversalMovement = await postCtx.StockMovements
                .SingleAsync(m => m.MovementType == StockMovementType.PurchaseReversal && m.ReferenceDocumentId == invoiceA.Id);

            Assert.Equal(-100m, reversalMovement.QuantityDelta);
            Assert.Equal(100m, reversalMovement.BalanceAfter);
            Assert.Equal(10m, reversalMovement.UnitCost);
            Assert.Equal(10m, reversalMovement.InventoryCostRate);
            Assert.Equal(-1000m, reversalMovement.InventoryValueDelta);
            Assert.Equal(2000m, reversalMovement.InventoryValueAfter);
            Assert.NotNull(reversalMovement.ReversesStockMovementId);
        }
    }

    [Fact]
    public async Task TestB_PurchaseCancellation_FullDepletion_EnforcesZeroBalanceAndValuationInvariant()
    {
        // Scenario: Full depletion to 0 stock must strictly reset InventoryValue = 0 and AveragePurchaseCost = 0
        var (productId, supplierId) = await SeedProductAndSupplierAsync();
        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var invoice = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-DEP-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-DEP",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-DEP-1",
                    ExpiryDate = exp,
                    Quantity = 50m,
                    PurchaseRate = 80m
                }
            ]
        });

        await writer.CancelPurchaseInvoiceAsync(invoice.Id, "Full cancellation test");

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.SingleAsync(b => b.ProductId == productId && b.NormalizedBatchNumber == "BATCH-DEP-1");
        Assert.Equal(0m, batch.QuantityOnHand);
        Assert.Equal(0m, batch.InventoryValue);
        Assert.Equal(0m, batch.AveragePurchaseCost);

        var reversal = await context.StockMovements.SingleAsync(m => m.MovementType == StockMovementType.PurchaseReversal && m.ReferenceDocumentId == invoice.Id);
        Assert.Equal(-50m, reversal.QuantityDelta);
        Assert.Equal(0m, reversal.BalanceAfter);
        Assert.Equal(80m, reversal.UnitCost);
        Assert.Equal(80m, reversal.InventoryCostRate);
        Assert.Equal(-4000m, reversal.InventoryValueDelta);
        Assert.Equal(0m, reversal.InventoryValueAfter);
    }

    [Fact]
    public async Task TestC_LinkedPurchaseReturn_ValuationAuditFields_PreservedAccurately()
    {
        var (productId, supplierId) = await SeedProductAndSupplierAsync();
        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var invoice = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-LNK-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-LNK",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-LNK-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 50m
                }
            ]
        });

        var returnDto = await writer.CreateAndPostPurchaseReturnAsync("PRET-2026-LNK1", new PurchaseReturnCreateDto
        {
            SupplierId = supplierId,
            OriginalPurchaseInvoiceId = invoice.Id,
            ReturnDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseReturnItemCreateDto
                {
                    OriginalPurchaseInvoiceItemId = invoice.Items[0].Id,
                    ProductId = productId,
                    ProductBatchId = invoice.Items[0].ProductBatchId,
                    Quantity = 20m
                }
            ]
        });

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.SingleAsync(b => b.Id == invoice.Items[0].ProductBatchId);
        Assert.Equal(80m, batch.QuantityOnHand);
        Assert.Equal(4000m, batch.InventoryValue); // 5000 - (20 * 50) = 4000
        Assert.Equal(50m, batch.AveragePurchaseCost);

        var returnMovement = await context.StockMovements
            .SingleAsync(m => m.MovementType == StockMovementType.PurchaseReturn && m.ReferenceDocumentId == returnDto.Id);

        Assert.Equal(-20m, returnMovement.QuantityDelta);
        Assert.Equal(80m, returnMovement.BalanceAfter);
        Assert.Equal(50m, returnMovement.UnitCost);
        Assert.Equal(50m, returnMovement.InventoryCostRate);
        Assert.Equal(-1000m, returnMovement.InventoryValueDelta);
        Assert.Equal(4000m, returnMovement.InventoryValueAfter);

        var line = await context.PurchaseInvoiceItems.SingleAsync(l => l.Id == invoice.Items[0].Id);
        Assert.Equal(20m, line.ReturnedQuantity);
    }

    [Fact]
    public async Task TestD_CustomerAndSupplierCode_UniquenessEnforced_RegardlessOfIsActive()
    {
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();

        // 1. Customer: Insert active, deactivate, attempt duplicate code
        var cust1 = new Customer
        {
            CustomerCode = "CUST-UNIQ",
            Name = "Active Customer",
            IsActive = true
        };
        await context.Customers.AddAsync(cust1);
        await context.SaveChangesAsync();

        cust1.IsActive = false;
        await context.SaveChangesAsync();

        var cust2 = new Customer
        {
            CustomerCode = "CUST-UNIQ",
            Name = "Duplicate Deactivated Code Customer",
            IsActive = true
        };
        await context.Customers.AddAsync(cust2);
        var custEx = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.True(DbExceptionHelper.IsUniqueConstraintViolation(custEx));

        // 2. Supplier: Insert active, deactivate, attempt duplicate code
        await using var context2 = await _fixture.ContextFactory.CreateDbContextAsync();
        var supp1 = new Supplier
        {
            SupplierCode = "SUPP-UNIQ",
            Name = "Active Supplier",
            IsActive = true
        };
        await context2.Suppliers.AddAsync(supp1);
        await context2.SaveChangesAsync();

        supp1.IsActive = false;
        await context2.SaveChangesAsync();

        var supp2 = new Supplier
        {
            SupplierCode = "SUPP-UNIQ",
            Name = "Duplicate Deactivated Code Supplier",
            IsActive = true
        };
        await context2.Suppliers.AddAsync(supp2);
        var suppEx = await Assert.ThrowsAsync<DbUpdateException>(() => context2.SaveChangesAsync());
        Assert.True(DbExceptionHelper.IsUniqueConstraintViolation(suppEx));
    }

    [Fact]
    public async Task TestE_CancelPurchaseReturn_RestoresStockAndValuation_RevertsReturnedQuantity_LinksReversalMovement()
    {
        var (productId, supplierId) = await SeedProductAndSupplierAsync();
        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var invoice = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-REV-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-REV",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-REV-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 60m
                }
            ]
        });

        var returnDto = await writer.CreateAndPostPurchaseReturnAsync("PRET-2026-REV1", new PurchaseReturnCreateDto
        {
            SupplierId = supplierId,
            OriginalPurchaseInvoiceId = invoice.Id,
            ReturnDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseReturnItemCreateDto
                {
                    OriginalPurchaseInvoiceItemId = invoice.Items[0].Id,
                    ProductId = productId,
                    ProductBatchId = invoice.Items[0].ProductBatchId,
                    Quantity = 30m
                }
            ]
        });

        // Cancel the purchase return
        await writer.CancelPurchaseReturnAsync(returnDto.Id, "Supplier rejected return due to package intactness");

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();

        // 1. Verify PurchaseReturn status
        var updatedReturn = await context.PurchaseReturns.SingleAsync(r => r.Id == returnDto.Id);
        Assert.Equal(PurchaseReturnStatus.Cancelled, updatedReturn.Status);
        Assert.NotNull(updatedReturn.CancelledAtUtc);
        Assert.Contains("Supplier rejected return", updatedReturn.Reason);

        // 2. Verify batch stock and valuation restored
        var batch = await context.ProductBatches.SingleAsync(b => b.Id == invoice.Items[0].ProductBatchId);
        Assert.Equal(100m, batch.QuantityOnHand);
        Assert.Equal(6000m, batch.InventoryValue);
        Assert.Equal(60m, batch.AveragePurchaseCost);

        // 3. Verify invoice item returned quantity reverted
        var invoiceLine = await context.PurchaseInvoiceItems.SingleAsync(l => l.Id == invoice.Items[0].Id);
        Assert.Equal(0m, invoiceLine.ReturnedQuantity);

        // 4. Verify compensating PurchaseReturnReversal movement
        var returnMovement = await context.StockMovements
            .SingleAsync(m => m.MovementType == StockMovementType.PurchaseReturn && m.ReferenceDocumentId == returnDto.Id);

        var reversalMovement = await context.StockMovements
            .SingleAsync(m => m.MovementType == StockMovementType.PurchaseReturnReversal && m.ReferenceDocumentId == returnDto.Id);

        Assert.Equal(30m, reversalMovement.QuantityDelta);
        Assert.Equal(100m, reversalMovement.BalanceAfter);
        Assert.Equal(60m, reversalMovement.UnitCost);
        Assert.Equal(60m, reversalMovement.InventoryCostRate);
        Assert.Equal(1800m, reversalMovement.InventoryValueDelta);
        Assert.Equal(6000m, reversalMovement.InventoryValueAfter);
        Assert.Equal(returnMovement.Id, reversalMovement.ReversesStockMovementId);
    }

    [Fact]
    public async Task CancelPurchaseInvoice_WithLinkedPurchaseReturn_RejectsUntilReturnCancelled()
    {
        // Scenario:
        // - Purchase A: 100 units @ 10
        // - Post Purchase Return against A: 20 units
        // - Purchase B into same batch: +100 units
        // - Current batch stock is therefore sufficient to reverse 100
        // - Attempt Cancel Purchase A -> EXPECTED: rejected because Purchase A has an active linked return
        // Then:
        // - Cancel/reverse the Purchase Return -> ReturnedQuantity becomes 0
        // - Retry Purchase A cancellation -> EXPECTED: allowed if all other downstream-consumption rules are satisfied
        var (productId, supplierId) = await SeedProductAndSupplierAsync();
        var writer = new PurchaseTransactionWriter(_fixture.ContextFactory, NullLogger<PurchaseTransactionWriter>.Instance);
        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

        var invoiceA = await writer.CreateAndPostPurchaseInvoiceAsync("PINV-LCG-001", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-LCG-A",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-LCG-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 10m
                }
            ]
        });

        var returnDto = await writer.CreateAndPostPurchaseReturnAsync("PRET-2026-LCG1", new PurchaseReturnCreateDto
        {
            SupplierId = supplierId,
            OriginalPurchaseInvoiceId = invoiceA.Id,
            ReturnDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseReturnItemCreateDto
                {
                    OriginalPurchaseInvoiceItemId = invoiceA.Items[0].Id,
                    ProductId = productId,
                    ProductBatchId = invoiceA.Items[0].ProductBatchId,
                    Quantity = 20m
                }
            ]
        });

        // Purchase B into same batch replenishing stock (+100)
        await writer.CreateAndPostPurchaseInvoiceAsync("PINV-LCG-002", new PurchaseInvoiceCreateDto
        {
            SupplierId = supplierId,
            SupplierInvoiceNumber = "BILL-LCG-B",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = productId,
                    BatchNumber = "BATCH-LCG-1",
                    ExpiryDate = exp,
                    Quantity = 100m,
                    PurchaseRate = 20m
                }
            ]
        });

        // Verify current batch stock is sufficient (80 + 100 = 180 >= 100)
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var batch = await ctx.ProductBatches.SingleAsync(b => b.Id == invoiceA.Items[0].ProductBatchId);
            Assert.Equal(180m, batch.QuantityOnHand);
        }

        // Attempt to cancel Purchase A -> EXPECTED: rejected with client-friendly message
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            writer.CancelPurchaseInvoiceAsync(invoiceA.Id, "Attempt cancel while active return exists"));

        Assert.Equal("This purchase has supplier returns recorded against it. Cancel those purchase returns first, then try cancelling the purchase again.", ex.Message);

        // Cancel/reverse the Purchase Return
        await writer.CancelPurchaseReturnAsync(returnDto.Id, "Reversing return to allow invoice cancellation");

        // Verify ReturnedQuantity becomes 0 and batch stock is 200
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var line = await ctx.PurchaseInvoiceItems.SingleAsync(l => l.Id == invoiceA.Items[0].Id);
            Assert.Equal(0m, line.ReturnedQuantity);

            var batch = await ctx.ProductBatches.SingleAsync(b => b.Id == invoiceA.Items[0].ProductBatchId);
            Assert.Equal(200m, batch.QuantityOnHand);
        }

        // Retry Purchase A cancellation -> EXPECTED: allowed and succeeds
        await writer.CancelPurchaseInvoiceAsync(invoiceA.Id, "Retry cancelling invoice after return was cancelled");

        // Verify Purchase A is cancelled and remaining stock is 100
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cancelledInvoice = await ctx.PurchaseInvoices.SingleAsync(i => i.Id == invoiceA.Id);
            Assert.Equal(PurchaseInvoiceStatus.Cancelled, cancelledInvoice.Status);

            var batch = await ctx.ProductBatches.SingleAsync(b => b.Id == invoiceA.Items[0].ProductBatchId);
            Assert.Equal(100m, batch.QuantityOnHand);
            Assert.Equal(2000m, batch.InventoryValue);
            Assert.Equal(20m, batch.AveragePurchaseCost);
        }
    }
}


