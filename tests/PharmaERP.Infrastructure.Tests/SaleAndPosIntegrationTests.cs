using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;
using PharmaERP.Infrastructure.Persistence.Helpers;
using PharmaERP.Infrastructure.Persistence.Repositories;
using PharmaERP.Infrastructure.Persistence.Services;

namespace PharmaERP.Infrastructure.Tests;

[Collection("SqlServerDatabaseCollection")]
public class SaleAndPosIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerTestFixture _fixture;

    public SaleAndPosIntegrationTests(SqlServerTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int ProductId, int Batch1Id, int Batch2Id)> SeedProductAndTwoBatchesAsync(
        decimal b1Qty = 10m, decimal b1Cost = 100m, DateOnly? b1Exp = null,
        decimal b2Qty = 20m, decimal b2Cost = 120m, DateOnly? b2Exp = null)
    {
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();

        var product = new Product
        {
            Name = "Amoxicillin 500mg",
            ProductCode = "AMOX-500",
            Barcode = "8901234567890",
            GenericName = "Amoxicillin",
            DefaultPurchasePrice = 100m,
            DefaultSalePrice = 150m,
            IsActive = true
        };
        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();

        var exp1 = b1Exp ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
        var exp2 = b2Exp ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(6));

        var batch1 = new ProductBatch
        {
            ProductId = product.Id,
            BatchNumber = "B-001",
            NormalizedBatchNumber = "B-001",
            ExpiryDate = exp1,
            QuantityOnHand = b1Qty,
            InventoryValue = b1Qty * b1Cost,
            AveragePurchaseCost = b1Cost,
            LastPurchaseCost = b1Cost,
            SuggestedSalePrice = 150m,
            IsActive = true
        };

        var batch2 = new ProductBatch
        {
            ProductId = product.Id,
            BatchNumber = "B-002",
            NormalizedBatchNumber = "B-002",
            ExpiryDate = exp2,
            QuantityOnHand = b2Qty,
            InventoryValue = b2Qty * b2Cost,
            AveragePurchaseCost = b2Cost,
            LastPurchaseCost = b2Cost,
            SuggestedSalePrice = 150m,
            IsActive = true
        };

        await context.ProductBatches.AddRangeAsync(batch1, batch2);
        await context.SaveChangesAsync();

        return (product.Id, batch1.Id, batch2.Id);
    }

    private SaleTransactionWriter CreateWriter()
    {
        return _fixture.CreateSaleWriter();
    }

    [Fact]
    public async Task ScenarioA_MultiBatchFefo_Depletion_EarliestExpiryFirst()
    {
        var (productId, batch1Id, batch2Id) = await SeedProductAndTwoBatchesAsync(
            b1Qty: 10m, b1Cost: 100m, b1Exp: DateOnly.FromDateTime(DateTime.Today.AddMonths(2)),
            b2Qty: 20m, b2Cost: 120m, b2Exp: DateOnly.FromDateTime(DateTime.Today.AddMonths(5)));

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 2500m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    Quantity = 15m, // Needs 10 from batch1, 5 from batch2
                    UnitSalePrice = 150m
                }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000001", saleDto);

        Assert.Equal(SaleInvoiceStatus.Posted, invoice.Status);
        Assert.Single(invoice.Items);
        var item = invoice.Items[0];
        Assert.Equal(2, item.BatchAllocations.Count);

        // First allocation must be Batch 1 (earliest expiry)
        var alloc1 = item.BatchAllocations.Single(a => a.ProductBatchId == batch1Id);
        Assert.Equal(10m, alloc1.Quantity);
        Assert.Equal(100m, alloc1.InventoryCostRate);
        Assert.Equal(1000m, alloc1.InventoryValueConsumed);

        // Second allocation must be Batch 2
        var alloc2 = item.BatchAllocations.Single(a => a.ProductBatchId == batch2Id);
        Assert.Equal(5m, alloc2.Quantity);
        Assert.Equal(120m, alloc2.InventoryCostRate);
        Assert.Equal(600m, alloc2.InventoryValueConsumed);

        // Check DB batches
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var b1 = await context.ProductBatches.FindAsync(batch1Id);
        var b2 = await context.ProductBatches.FindAsync(batch2Id);

        Assert.NotNull(b1);
        Assert.Equal(0m, b1.QuantityOnHand);
        Assert.Equal(0m, b1.InventoryValue);

        Assert.NotNull(b2);
        Assert.Equal(15m, b2.QuantityOnHand);
        Assert.Equal(15m * 120m, b2.InventoryValue);
    }

    [Fact]
    public async Task ScenarioB_ExpiredBatch_ExcludedFromAutomaticFefo()
    {
        // Batch 1 is expired yesterday; Batch 2 is valid
        var (productId, batch1Id, batch2Id) = await SeedProductAndTwoBatchesAsync(
            b1Qty: 10m, b1Cost: 100m, b1Exp: DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            b2Qty: 10m, b2Cost: 120m, b2Exp: DateOnly.FromDateTime(DateTime.Today.AddMonths(6)));

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 1000m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    Quantity = 5m,
                    UnitSalePrice = 150m
                }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000002", saleDto);

        var alloc = Assert.Single(invoice.Items[0].BatchAllocations);
        Assert.Equal(batch2Id, alloc.ProductBatchId); // Must pick batch2, skipping expired batch1
        Assert.Equal(5m, alloc.Quantity);

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var b1 = await context.ProductBatches.FindAsync(batch1Id);
        Assert.NotNull(b1);
        Assert.Equal(10m, b1.QuantityOnHand); // Untouched!
    }

    [Fact]
    public async Task ScenarioC_AuthoritativeCogs_CapturedFromSqlOutputDelta()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 85.50m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 500m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 4m, UnitSalePrice = 120m }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000003", saleDto);
        var alloc = Assert.Single(invoice.Items[0].BatchAllocations);

        Assert.Equal(85.50m, alloc.InventoryCostRate);
        Assert.Equal(342.00m, alloc.InventoryValueConsumed); // 4 * 85.50

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var movement = await context.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batch1Id && m.MovementType == StockMovementType.Sale);
        Assert.NotNull(movement);
        Assert.Equal(-4m, movement.QuantityDelta);
        Assert.Equal(-342.00m, movement.InventoryValueDelta);
        Assert.Equal(85.50m, movement.InventoryCostRate);
    }

    [Fact]
    public async Task ScenarioD_ConcurrentCashierRace_PreventsOverDepletion()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        // Cashier 1 requests 8 units, Cashier 2 requests 8 units (only 10 total in stock)
        var dto1 = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 8m, UnitSalePrice = 150m } }
        };
        var dto2 = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 8m, UnitSalePrice = 150m } }
        };

        var task1 = Task.Run(() => writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000004A", dto1));
        var task2 = Task.Run(() => writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000004B", dto2));

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)));

        // Exactly one should succeed, and one should fail with InsufficientStockException
        int successCount = results.Count(r => r.Success);
        int failureCount = results.Count(r => !r.Success && r.Exception is InsufficientStockException);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        // Final stock must be exactly 2, never negative
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.FindAsync(batch1Id);
        Assert.NotNull(batch);
        Assert.Equal(2m, batch.QuantityOnHand);
    }

    [Fact]
    public async Task ScenarioE_CumulativeSalesReturn_DatabaseGuard_BlocksOverReturn()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 150m } }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000005", saleDto);
        var saleItemId = invoice.Items[0].Id;

        // First partial return: 6 units
        var ret1Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Damaged",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = saleItemId, Quantity = 6m } }
        };
        var ret1 = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000001", ret1Dto);
        Assert.Equal(SaleReturnStatus.Posted, ret1.Status);

        // Second return: 5 units (6 + 5 = 11 > 10) -> Must fail at DB atomic guard
        var ret2Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Over return",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = saleItemId, Quantity = 5m } }
        };

        await Assert.ThrowsAsync<ValidationException>(() => writer.CreateAndPostSaleReturnAsync("SRET-202609-000002", ret2Dto));

        // ReturnedQuantity in DB must still be 6
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var dbItem = await context.SaleInvoiceItems.FindAsync(saleItemId);
        Assert.NotNull(dbItem);
        Assert.Equal(6m, dbItem.ReturnedQuantity);
    }

    [Fact]
    public async Task ScenarioF_ExactOriginalBatchAndInventoryValueRestoration_OnReturn()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 150m } }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000006", saleDto);

        // Batch stock is 0 now
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.Equal(0m, b!.QuantityOnHand);
        }

        // Return 4 units
        var retDto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Not needed",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 4m } }
        };
        await writer.CreateAndPostSaleReturnAsync("SRET-202609-000003", retDto);

        // Batch stock restored to 4, value restored to 4 * 100 = 400
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.Equal(4m, b!.QuantityOnHand);
            Assert.Equal(400m, b.InventoryValue);

            var movement = await ctx.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batch1Id && m.MovementType == StockMovementType.SaleReturn);
            Assert.NotNull(movement);
            Assert.Equal(4m, movement.QuantityDelta);
            Assert.Equal(400m, movement.InventoryValueDelta);
        }
    }

    [Fact]
    public async Task ScenarioG_ZeroBalanceInvariant_MaintainedOnFullDepletion()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 5m, b1Cost: 77.33m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1000m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 5m, UnitSalePrice = 120m } }
        };

        await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000007", saleDto);

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.FindAsync(batch1Id);
        Assert.NotNull(batch);
        Assert.Equal(0m, batch.QuantityOnHand);
        Assert.Equal(0m, batch.InventoryValue);
    }

    [Fact]
    public async Task ScenarioH_DownstreamConsumptionGuard_OnSaleReturnCancellation()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        // 1. Sell 10 units (batch reaches 0)
        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 150m } }
        };
        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000008A", saleDto);

        // 2. Return 5 units (batch reaches 5)
        var retDto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Customer return",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 5m } }
        };
        var ret = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000004", retDto);

        // 3. Downstream Sale 2 consumes 4 units from this returned batch (batch reaches 1)
        var sale2Dto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1000m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 4m, UnitSalePrice = 150m } }
        };
        await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000008B", sale2Dto);

        // 4. Try to cancel Return (needs 5 units to revert, but only 1 available)
        await Assert.ThrowsAsync<ValidationException>(() => writer.CancelSaleReturnAsync(ret.Id, "Cancel return test"));
    }

    [Fact]
    public async Task ScenarioI_OperationId_IdempotencyRecovery_OnRetry()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        var opId = Guid.NewGuid();
        var saleDto = new SaleInvoiceCreateDto
        {
            OperationId = opId,
            SaleType = SaleType.Cash,
            TenderedAmount = 500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 3m, UnitSalePrice = 150m } }
        };

        // First call
        var inv1 = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000009", saleDto);

        // Second call with same OperationId (retry)
        var inv2 = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000009-DUP", saleDto);

        Assert.Equal(inv1.Id, inv2.Id);
        Assert.Equal(inv1.InvoiceNumber, inv2.InvoiceNumber);

        // Ensure stock was only decremented ONCE (10 - 3 = 7)
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.FindAsync(batch1Id);
        Assert.NotNull(batch);
        Assert.Equal(7m, batch.QuantityOnHand);
    }

    [Fact]
    public async Task ScenarioJ_StaleFefoPreview_ReevaluatesAtPosting()
    {
        // Batch 1 has 5 units (exp in 2 months), Batch 2 has 10 units (exp in 5 months)
        var (productId, batch1Id, batch2Id) = await SeedProductAndTwoBatchesAsync(
            b1Qty: 5m, b1Cost: 100m, b1Exp: DateOnly.FromDateTime(DateTime.Today.AddMonths(2)),
            b2Qty: 10m, b2Cost: 120m, b2Exp: DateOnly.FromDateTime(DateTime.Today.AddMonths(5)));

        var writer = CreateWriter();

        // Suppose client saw preview allocating Batch 1 (5) + Batch 2 (3).
        // Concurrently, another transaction depletes Batch 1 completely to 0.
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            b1!.QuantityOnHand = 0m;
            b1.InventoryValue = 0m;
            await ctx.SaveChangesAsync();
        }

        // Now post sale request for Qty 8 (without explicit batch, normal FEFO)
        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1200m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 8m, UnitSalePrice = 150m }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000014", saleDto);

        // Writer must have dynamically re-evaluated in-transaction and allocated all 8 from Batch 2!
        Assert.Single(invoice.Items);
        var item = invoice.Items[0];
        Assert.Single(item.BatchAllocations);
        Assert.Equal(batch2Id, item.BatchAllocations[0].ProductBatchId);
        Assert.Equal(8m, item.BatchAllocations[0].Quantity);

        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b2 = await ctx.ProductBatches.FindAsync(batch2Id);
            Assert.Equal(2m, b2!.QuantityOnHand); // 10 - 8 = 2
        }
    }

    [Fact]
    public async Task ScenarioK_FinalDepletion_CogsRoundingReconcilesExactly()
    {
        // Qty 3, Value 28.0000, Avg 9.3333
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(
            b1Qty: 3m, b1Cost: 9.3333m, b2Qty: 0m);

        // Explicitly set exact inventory value to 28.0000 to test rounding reconciliation
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            b1!.InventoryValue = 28.0000m;
            b1.AveragePurchaseCost = 9.3333m;
            await ctx.SaveChangesAsync();
        }

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 100m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 3m, UnitSalePrice = 20m }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000015", saleDto);

        var alloc = invoice.Items[0].BatchAllocations[0];
        Assert.Equal(3m, alloc.Quantity);
        Assert.Equal(9.3333m, alloc.InventoryCostRate);
        Assert.Equal(28.0000m, alloc.InventoryValueConsumed); // Full 28.0000 consumed

        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.NotNull(b1);
            Assert.Equal(0.0000m, b1.QuantityOnHand);
            Assert.Equal(0.0000m, b1.InventoryValue);
            Assert.Equal(0.0000m, b1.AveragePurchaseCost);

            var sm = await ctx.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batch1Id && m.MovementType == StockMovementType.Sale);
            Assert.NotNull(sm);
            Assert.Equal(-3m, sm.QuantityDelta);
            Assert.Equal(-28.0000m, sm.InventoryValueDelta);
            Assert.Equal(9.3333m, sm.InventoryCostRate);
        }
    }

    [Fact]
    public async Task ScenarioL_PartialReturns_RestoreExactOriginalInventoryValue()
    {
        // Sell 3 units with non-trivial fractional cost: Value 28.0000 (rate 9.3333)
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(
            b1Qty: 3m, b1Cost: 9.3333m, b2Qty: 0m);

        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            b1!.InventoryValue = 28.0000m;
            b1.AveragePurchaseCost = 9.3333m;
            await ctx.SaveChangesAsync();
        }

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 60m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 3m, UnitSalePrice = 20m }
            }
        };
        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000016", saleDto);
        var originalAlloc = invoice.Items[0].BatchAllocations[0];
        Assert.Equal(28.0000m, originalAlloc.InventoryValueConsumed);

        // Return 1: 1 unit
        var ret1Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Return part 1",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };
        var ret1 = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000007", ret1Dto);
        var rAlloc1 = ret1.Items[0].BatchAllocations[0];
        Assert.Equal(9.3333m, rAlloc1.RestoredInventoryValue);

        // Return 2: 1 unit
        var ret2Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Return part 2",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };
        var ret2 = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000008", ret2Dto);
        var rAlloc2 = ret2.Items[0].BatchAllocations[0];
        Assert.Equal(9.3333m, rAlloc2.RestoredInventoryValue);

        // Return 3: final 1 unit (absorbs remainder!)
        var ret3Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Return part 3",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };
        var ret3 = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000009", ret3Dto);
        var rAlloc3 = ret3.Items[0].BatchAllocations[0];
        // 28.0000 - 9.3333 - 9.3333 = 9.3334
        Assert.Equal(9.3334m, rAlloc3.RestoredInventoryValue);

        // Total restored equals exact original InventoryValueConsumed: 28.0000
        decimal totalRestored = rAlloc1.RestoredInventoryValue + rAlloc2.RestoredInventoryValue + rAlloc3.RestoredInventoryValue;
        Assert.Equal(28.0000m, totalRestored);

        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.NotNull(b1);
            Assert.Equal(3.0000m, b1.QuantityOnHand);
            Assert.Equal(28.0000m, b1.InventoryValue);
        }
    }

    [Fact]
    public async Task ScenarioL2_FinancialPartialReturnRefundCalculation_ReflectsDiscounts()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        // Gross 200 (2 items @ 100), invoice discount 20 -> Net Total 180 (effective price 90 per item)
        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 200m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 2m, UnitSalePrice = 100m } },
            InvoiceDiscountAmount = 20m
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000010", saleDto);
        Assert.Equal(180m, invoice.NetTotal);

        // Return 1 item -> refund should be 90 (proportional discount applied!)
        var retDto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Return one",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };

        var ret = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000005", retDto);
        Assert.Equal(90m, ret.TotalAmount);
    }

    [Fact]
    public async Task ScenarioM_ConcurrentReturns_CannotOverRefundNetLineAmount()
    {
        var (productId, _, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 5m, b1Cost: 50m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 100m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 100m } }
        };
        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000017", saleDto);

        // Return 1 succeeds
        var ret1Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "First return",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };
        await writer.CreateAndPostSaleReturnAsync("SRET-202609-000010", ret1Dto);

        // Return 2 tries to return another unit or over-refund -> DB check / validation rejects!
        var ret2Dto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Second return attempt",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 1m } }
        };
        await Assert.ThrowsAsync<ValidationException>(() => writer.CreateAndPostSaleReturnAsync("SRET-202609-000011", ret2Dto));
    }

    [Fact]
    public async Task ScenarioM2_SaleCancellation_RestoresBatchStockAndCost()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 600m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 4m, UnitSalePrice = 150m } }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000011", saleDto);

        // Cancel sale
        await writer.CancelSaleInvoiceAsync(invoice.Id, "Customer cancelled before leaving");

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var inv = await context.SaleInvoices.FindAsync(invoice.Id);
        Assert.NotNull(inv);
        Assert.Equal(SaleInvoiceStatus.Cancelled, inv.Status);

        var batch = await context.ProductBatches.FindAsync(batch1Id);
        Assert.NotNull(batch);
        Assert.Equal(10m, batch.QuantityOnHand);
        Assert.Equal(1000m, batch.InventoryValue);

        var revMovement = await context.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batch1Id && m.MovementType == StockMovementType.SaleReversal);
        Assert.NotNull(revMovement);
        Assert.Equal(4m, revMovement.QuantityDelta);
        Assert.Equal(400m, revMovement.InventoryValueDelta);
    }

    [Fact]
    public async Task ScenarioN_SaleCancellation_FromZeroCurrentStock_RestoresInventory()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 50m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1000m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 100m } }
        };
        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000018", saleDto);

        // Verify batch is 0
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.Equal(0m, b1!.QuantityOnHand);
            Assert.Equal(0m, b1.InventoryValue);
        }

        // Cancel sale
        await writer.CancelSaleInvoiceAsync(invoice.Id, "Voided sale at zero stock");

        // Verify stock is restored cleanly as INFLOW
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            Assert.NotNull(b1);
            Assert.Equal(10m, b1.QuantityOnHand);
            Assert.Equal(500m, b1.InventoryValue);
            Assert.Equal(50m, b1.AveragePurchaseCost);

            var sm = await ctx.StockMovements.SingleOrDefaultAsync(m => m.ProductBatchId == batch1Id && m.MovementType == StockMovementType.SaleReversal);
            Assert.NotNull(sm);
            Assert.Equal(10m, sm.QuantityDelta);
            Assert.Equal(500m, sm.InventoryValueDelta);
        }
    }

    [Fact]
    public async Task ScenarioN2_SaleCancellation_Blocked_WhenActiveSalesReturnExists()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 100m, b2Qty: 0m);

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 150m } }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000012", saleDto);

        // Record a return
        var retDto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = invoice.Id,
            Reason = "Return partial",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = invoice.Items[0].Id, Quantity = 2m } }
        };
        await writer.CreateAndPostSaleReturnAsync("SRET-202609-000006", retDto);

        // Attempt to cancel original sale invoice -> blocked because active returns exist!
        await Assert.ThrowsAsync<ValidationException>(() => writer.CancelSaleInvoiceAsync(invoice.Id, "Attempt cancel with return"));
    }

    [Fact]
    public async Task ScenarioO_CancelSaleReturn_RejectsAfterDownstreamConsumptionEvenAfterReplenishment()
    {
        var (productId, batch1Id, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 50m, b2Qty: 0m);

        var writer = CreateWriter();

        // 1. Sell 10 units (batch drops to 0)
        var saleDto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 1000m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 10m, UnitSalePrice = 100m } }
        };
        var inv1 = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000019A", saleDto);

        // 2. Return 5 units (batch has 5)
        var retDto = new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = inv1.Id,
            Reason = "Return 5",
            Items = new List<SaleReturnItemCreateDto> { new() { OriginalSaleInvoiceItemId = inv1.Items[0].Id, Quantity = 5m } }
        };
        var ret = await writer.CreateAndPostSaleReturnAsync("SRET-202609-000012", retDto);

        // 3. Downstream sale sells 4 units from this returned batch (stock was consumed!)
        var sale2Dto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Cash,
            TenderedAmount = 500m,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 4m, UnitSalePrice = 100m } }
        };
        await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000019B", sale2Dto);

        // 4. A subsequent replenishment arrives (e.g. Purchase adds 20 units to batch)
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var b1 = await ctx.ProductBatches.FindAsync(batch1Id);
            b1!.QuantityOnHand += 20m;
            b1.InventoryValue += 1000m;
            await ctx.SaveChangesAsync();
        }

        // Current batch stock is now 21 units (plenty to cover 5 units!)
        // BUT downstream consumption already occurred on the returned units!
        // Cancelling return MUST BE REJECTED!
        var ex = await Assert.ThrowsAsync<ValidationException>(() => writer.CancelSaleReturnAsync(ret.Id, "Attempt cancel after downstream sale"));
        Assert.Contains("already been consumed", ex.Message);
    }

    [Fact]
    public async Task ScenarioP_DocumentNumberGenerator_ConcurrentSalesAndReturns_GeneratesUniqueNumbers()
    {
        var docGen = new DocumentNumberGenerator(_fixture.ContextFactory, NullLogger<DocumentNumberGenerator>.Instance);

        var saleTasks = Enumerable.Range(0, 10).Select(_ => docGen.NextDocumentNumberAsync(DocumentType.SaleInvoice));
        var returnTasks = Enumerable.Range(0, 10).Select(_ => docGen.NextDocumentNumberAsync(DocumentType.SaleReturn));

        var saleNums = await Task.WhenAll(saleTasks);
        var returnNums = await Task.WhenAll(returnTasks);

        Assert.Equal(10, saleNums.Distinct().Count());
        Assert.Equal(10, returnNums.Distinct().Count());

        Assert.All(saleNums, num => Assert.StartsWith("SINV-", num));
        Assert.All(returnNums, num => Assert.StartsWith("SRET-", num));
    }

    [Fact]
    public async Task ScenarioQ_HistoricalPrintSnapshot_RemainsImmutable()
    {
        var (productId, _, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b1Cost: 50m, b2Qty: 0m);

        int customerId;
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cust = new Customer
            {
                CustomerCode = "CUST-SNAP",
                Name = "Original Snapshot Customer",
                IsActive = true
            };
            await ctx.Customers.AddAsync(cust);
            await ctx.SaveChangesAsync();
            customerId = cust.Id;
        }

        var configRepo = new AppConfigRepository(_fixture.ContextFactory, NullLogger<AppConfigRepository>.Instance);
        await configRepo.SaveBusinessProfileAsync(new BusinessProfileDto
        {
            PharmacyName = "Original Snapshot Pharmacy",
            Address = "123 Original Road",
            Phone = "0300-1111111",
            ReceiptFooter = "Original Receipt Footer"
        });

        var writer = CreateWriter();

        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = customerId,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 2m, UnitSalePrice = 100m }
            }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000020", saleDto);

        // Now mutate the masters in database
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cust = await ctx.Customers.FindAsync(customerId);
            cust!.Name = "MUTATED CUSTOMER NAME";

            var prod = await ctx.Products.FindAsync(productId);
            prod!.Name = "MUTATED PRODUCT NAME";

            await ctx.SaveChangesAsync();
        }

        await configRepo.SaveBusinessProfileAsync(new BusinessProfileDto
        {
            PharmacyName = "MUTATED PHARMACY NAME",
            Address = "999 Mutated Boulevard",
            Phone = "0300-9999999",
            ReceiptFooter = "MUTATED FOOTER"
        });

        // Load historical print data
        var saleRepo = new SaleInvoiceRepository(_fixture.ContextFactory, NullLogger<SaleInvoiceRepository>.Instance);
        var printProvider = new InvoicePrintDataProvider(saleRepo, configRepo);
        var printData = await printProvider.GetPrintDataAsync(invoice.Id);

        // Assert snapshots remain immutable!
        Assert.Equal("Original Snapshot Pharmacy", printData.PharmacyName);
        Assert.Equal("123 Original Road", printData.PharmacyAddress);
        Assert.Equal("Original Receipt Footer", printData.ReceiptFooter);
        Assert.Equal("Original Snapshot Customer", printData.CustomerName);
        Assert.Equal("Amoxicillin 500mg", printData.Items[0].ProductName); // original product name
    }

    [Fact]
    public async Task ScenarioQ2_ProductLookupService_BarcodeAndCodeAndSearch()
    {
        var (productId, batch1Id, batch2Id) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b2Qty: 15m);

        var prodRepo = new ProductRepository(_fixture.ContextFactory, NullLogger<ProductRepository>.Instance);
        var batchRepo = new ProductBatchRepository(_fixture.ContextFactory, NullLogger<ProductBatchRepository>.Instance);
        var configRepo = new AppConfigRepository(_fixture.ContextFactory, NullLogger<AppConfigRepository>.Instance);
        var clock = new BusinessClock(_fixture.ContextFactory, configRepo);
        var lookupService = new ProductLookupService(prodRepo, batchRepo, clock);

        // 1. By exact barcode
        var res1 = await lookupService.LookupByBarcodeOrCodeAsync("8901234567890");
        Assert.NotNull(res1.Product);
        Assert.Equal(productId, res1.Product.Id);
        Assert.Equal(25m, res1.TotalSaleableQuantity);

        // 2. By exact product code
        var res2 = await lookupService.LookupByBarcodeOrCodeAsync("AMOX-500");
        Assert.NotNull(res2.Product);
        Assert.Equal(productId, res2.Product.Id);

        // 3. By partial search
        var searchResults = await lookupService.SearchProductsAsync("Amox");
        Assert.Single(searchResults);
        Assert.NotNull(searchResults[0].Product);
        Assert.Equal(productId, searchResults[0].Product!.Id);
        Assert.Equal(25m, searchResults[0].TotalSaleableQuantity);
    }

    [Fact]
    public async Task ScenarioR_WalkInCustomer_AndCreditCustomer_Handling()
    {
        var (productId, _, _) = await SeedProductAndTwoBatchesAsync(b1Qty: 10m, b2Qty: 0m);

        int customerId;
        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cust = new Customer
            {
                CustomerCode = "CUST-001",
                Name = "John Doe",
                CreditLimit = 5000m,
                IsActive = true
            };
            await ctx.Customers.AddAsync(cust);
            await ctx.SaveChangesAsync();
            customerId = cust.Id;
        }

        var writer = CreateWriter();

        // Credit sale
        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = customerId,
            SaleType = SaleType.Credit,
            TenderedAmount = null,
            Items = new List<SaleInvoiceItemCreateDto> { new() { ProductId = productId, Quantity = 2m, UnitSalePrice = 150m } }
        };

        var invoice = await writer.CreateAndPostSaleInvoiceAsync("SINV-202609-000013", saleDto);
        Assert.Equal(SaleType.Credit, invoice.SaleType);
        Assert.Equal(300m, invoice.NetTotal);
        Assert.Equal("John Doe", invoice.CustomerNameSnapshot);
    }

    [Fact]
    public async Task ScenarioS_ProductLookupStates_DifferentiatesAllStatuses()
    {
        var prodRepo = new ProductRepository(_fixture.ContextFactory, NullLogger<ProductRepository>.Instance);
        var batchRepo = new ProductBatchRepository(_fixture.ContextFactory, NullLogger<ProductBatchRepository>.Instance);
        var configRepo = new AppConfigRepository(_fixture.ContextFactory, NullLogger<AppConfigRepository>.Instance);
        var clock = new BusinessClock(_fixture.ContextFactory, configRepo);
        var lookupService = new ProductLookupService(prodRepo, batchRepo, clock);

        int inactiveProdId;
        int outOfStockProdId;
        int expiredOnlyProdId;
        int availableProdId;

        await using (var ctx = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            // 1. Inactive product
            var pInactive = new Product { Name = "Inactive Med", ProductCode = "P-INACT", Barcode = "BAR-INACT", IsActive = false, DefaultSalePrice = 10m };
            // 2. Out of stock product (active, but 0 batches)
            var pOutOfStock = new Product { Name = "Out of Stock Med", ProductCode = "P-OOS", Barcode = "BAR-OOS", IsActive = true, DefaultSalePrice = 10m };
            // 3. Expired only product (active, has stock, but expired)
            var pExpired = new Product { Name = "Expired Only Med", ProductCode = "P-EXP", Barcode = "BAR-EXP", IsActive = true, DefaultSalePrice = 10m };
            // 4. Available product (active, has unexpired stock)
            var pAvail = new Product { Name = "Available Med", ProductCode = "P-AVAIL", Barcode = "BAR-AVAIL", IsActive = true, DefaultSalePrice = 10m };

            await ctx.Products.AddRangeAsync(pInactive, pOutOfStock, pExpired, pAvail);
            await ctx.SaveChangesAsync();

            inactiveProdId = pInactive.Id;
            outOfStockProdId = pOutOfStock.Id;
            expiredOnlyProdId = pExpired.Id;
            availableProdId = pAvail.Id;

            // Batch for expired:
            var bExp = new ProductBatch
            {
                ProductId = expiredOnlyProdId,
                BatchNumber = "EXP-1",
                NormalizedBatchNumber = "EXP-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), // Expired
                QuantityOnHand = 20m,
                InventoryValue = 200m,
                AveragePurchaseCost = 10m,
                LastPurchaseCost = 10m,
                SuggestedSalePrice = 15m,
                IsActive = true
            };

            // Batch for available:
            var bAvail = new ProductBatch
            {
                ProductId = availableProdId,
                BatchNumber = "AVAIL-1",
                NormalizedBatchNumber = "AVAIL-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), // Unexpired
                QuantityOnHand = 15m,
                InventoryValue = 150m,
                AveragePurchaseCost = 10m,
                LastPurchaseCost = 10m,
                SuggestedSalePrice = 15m,
                IsActive = true
            };

            await ctx.ProductBatches.AddRangeAsync(bExp, bAvail);
            await ctx.SaveChangesAsync();
        }

        // Test 1: NotFound
        var resNotFound = await lookupService.LookupByBarcodeOrCodeAsync("NON-EXISTENT-CODE");
        Assert.Equal(ProductLookupStatus.NotFound, resNotFound.Status);

        // Test 2: Inactive
        var resInactive = await lookupService.LookupByBarcodeOrCodeAsync("BAR-INACT");
        Assert.Equal(ProductLookupStatus.Inactive, resInactive.Status);

        // Test 3: OutOfStock
        var resOos = await lookupService.LookupByBarcodeOrCodeAsync("BAR-OOS");
        Assert.Equal(ProductLookupStatus.OutOfStock, resOos.Status);

        // Test 4: ExpiredOnly
        var resExp = await lookupService.LookupByBarcodeOrCodeAsync("BAR-EXP");
        Assert.Equal(ProductLookupStatus.ExpiredOnly, resExp.Status);

        // Test 5: Available
        var resAvail = await lookupService.LookupByBarcodeOrCodeAsync("BAR-AVAIL");
        Assert.Equal(ProductLookupStatus.Available, resAvail.Status);
        Assert.Equal(15m, resAvail.TotalSaleableQuantity);
    }
}
