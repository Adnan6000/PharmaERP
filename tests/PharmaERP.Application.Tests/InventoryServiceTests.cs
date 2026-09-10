using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class InventoryServiceTests
{
    private class FakeProductBatchRepo : IProductBatchRepository
    {
        public readonly List<ProductBatch> Batches = [];

        public Task<ProductBatch?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.Id == id));

        public Task<ProductBatch?> GetByNormalizedAsync(int productId, string normalizedBatchNumber, DateOnly expiryDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.ProductId == productId && b.NormalizedBatchNumber == normalizedBatchNumber && b.ExpiryDate == expiryDate));

        public Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var list = Batches.Where(b => b.ProductId == productId && b.QuantityOnHand > 0)
                .OrderBy(b => b.ExpiryDate)
                .Select(b => new ProductBatchDto(b.Id, b.ProductId, "Product", b.BatchNumber, b.NormalizedBatchNumber, b.ManufacturingDate, b.ExpiryDate, b.QuantityOnHand, b.InventoryValue, b.AveragePurchaseCost, b.LastPurchaseCost, b.SuggestedSalePrice, b.IsActive, b.ExpiryDate < today, b.ExpiryDate <= today.AddDays(90), b.ExpiryDate.DayNumber - today.DayNumber, b.RowVersion))
                .ToList();
            return Task.FromResult<IReadOnlyList<ProductBatchDto>>(list);
        }

        public Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<BatchStockDto>([], 0, 1, 20));

        public Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<CurrentStockDto>([], 0, 1, 20));

        public Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ExpiryReportItemDto>([], 0, 1, 20));

        public Task<decimal> GetTotalInventoryValuationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.Sum(b => b.InventoryValue));
    }

    private class FakeStockMovementRepo : IStockMovementRepository
    {
        public Task<PagedResult<StockMovementDto>> GetPagedAsync(PaginationQuery query, int? productId = null, int? batchId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<StockMovementDto>([], 0, 1, 20));

        public Task<bool> HasDownstreamOutflowsAsync(int batchId, DateTime afterUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private class FakeInventoryWriter : IInventoryTransactionWriter
    {
        public Task<ProductBatchDto> RecordOpeningStockAsync(OpeningStockCreateDto dto, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProductBatchDto(
                1,
                dto.ProductId,
                "Test Med",
                dto.BatchNumber,
                dto.BatchNumber.ToUpperInvariant(),
                dto.ManufacturingDate,
                dto.ExpiryDate,
                dto.Quantity,
                dto.Quantity * dto.UnitCost,
                dto.UnitCost,
                dto.UnitCost,
                dto.SuggestedSalePrice,
                true,
                false,
                false,
                100,
                []));
        }
    }

    [Fact]
    public async Task RecordOpeningStockAsync_Throws_WhenExpiredDate()
    {
        var service = new InventoryService(new FakeProductBatchRepo(), new FakeStockMovementRepo(), new FakeInventoryWriter());

        var dto = new OpeningStockCreateDto
        {
            ProductId = 1,
            BatchNumber = "B-001",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-5)),
            Quantity = 10,
            UnitCost = 15
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordOpeningStockAsync(dto));
    }

    [Fact]
    public async Task RecordOpeningStockAsync_Throws_WhenQuantityZeroOrNegative()
    {
        var service = new InventoryService(new FakeProductBatchRepo(), new FakeStockMovementRepo(), new FakeInventoryWriter());

        var dto = new OpeningStockCreateDto
        {
            ProductId = 1,
            BatchNumber = "B-001",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
            Quantity = 0,
            UnitCost = 15
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordOpeningStockAsync(dto));
    }

    [Fact]
    public async Task GetActiveFefoBatchesAsync_ReturnsBatchesInFefoOrder()
    {
        var batchRepo = new FakeProductBatchRepo();
        batchRepo.Batches.Add(new ProductBatch
        {
            Id = 1,
            ProductId = 1,
            BatchNumber = "B-2027",
            NormalizedBatchNumber = "B-2027",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
            QuantityOnHand = 10
        });
        batchRepo.Batches.Add(new ProductBatch
        {
            Id = 2,
            ProductId = 1,
            BatchNumber = "B-2026",
            NormalizedBatchNumber = "B-2026",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
            QuantityOnHand = 20
        });

        var service = new InventoryService(batchRepo, new FakeStockMovementRepo(), new FakeInventoryWriter());

        var fefoList = await service.GetActiveFefoBatchesAsync(1);

        Assert.Equal(2, fefoList.Count);
        Assert.Equal("B-2026", fefoList[0].BatchNumber); // Earlier expiry first
        Assert.Equal("B-2027", fefoList[1].BatchNumber);
    }
}

