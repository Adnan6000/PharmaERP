using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class ProductLookupAndPosTests
{
    private class FakeProductRepo : IProductRepository
    {
        public readonly List<Product> Products = new();

        public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

        public Task<PagedResult<ProductDto>> GetPagedAsync(
            PaginationQuery query,
            string? searchTerm = null,
            int? categoryId = null,
            int? manufacturerId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ProductDto>([], 0, 1, 20));

        public Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            product.Id = Products.Count + 1;
            Products.Add(product);
            return Task.FromResult(product);
        }

        public Task UpdateAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsBarcodeAsync(string barcode, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Barcode == barcode));

        public Task<Product?> GetByProductCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.ProductCode == code));

        public Task<List<Product>> SearchProductsAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                (p.GenericName != null && p.GenericName.Contains(query, StringComparison.OrdinalIgnoreCase))).Take(maxResults).ToList());
    }

    private class FakeBatchRepo : IProductBatchRepository
    {
        public readonly List<ProductBatch> Batches = new();

        public Task<ProductBatch?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.Id == id));

        public Task<ProductBatch?> GetByNormalizedAsync(int productId, string normalizedBatchNumber, DateOnly expiryDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductBatch?>(null);

        public Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductBatchDto>>(new List<ProductBatchDto>());

        public Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<BatchStockDto>([], 0, 1, 20));

        public Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<CurrentStockDto>([], 0, 1, 20));

        public Task<List<ProductBatch>> GetByProductIdAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.Where(b => b.ProductId == productId).ToList());

        public Task<List<ProductBatch>> GetEligibleBatchesForSaleAsync(int productId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.Where(b => b.ProductId == productId && b.QuantityOnHand > 0 && b.ExpiryDate > businessDate).ToList());

        public Task<List<ProductBatch>> GetEligibleBatchesForReturnAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.Where(b => b.ProductId == productId).ToList());

        public Task<ProductBatch?> GetByProductAndBatchNumberAsync(int productId, string batchNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.ProductId == productId && b.BatchNumber == batchNumber));

        public Task<ProductBatch> AddAsync(ProductBatch batch, CancellationToken cancellationToken = default)
        {
            batch.Id = Batches.Count + 1;
            Batches.Add(batch);
            return Task.FromResult(batch);
        }

        public Task UpdateAsync(ProductBatch batch, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<ProductBatch>> GetNearExpiryBatchesAsync(int nearExpiryDaysThreshold, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ProductBatch>());

        public Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ExpiryReportItemDto>([], 0, 1, 20));

        public Task<decimal> GetTotalInventoryValuationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);
    }

    private class FakeClock : IBusinessClock
    {
        public Task<DateTime> GetServerUtcNowAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc));

        public Task<DateOnly> GetBusinessDateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DateOnly(2026, 9, 11));

        public Task<DateTime> ToBusinessTimeAsync(DateTime utcDateTime, CancellationToken cancellationToken = default) =>
            Task.FromResult(utcDateTime.AddHours(5));

        public Task<DateOnly> ToBusinessDateAsync(DateTime utcDateTime, CancellationToken cancellationToken = default) =>
            Task.FromResult(DateOnly.FromDateTime(utcDateTime.AddHours(5)));
    }

    [Fact]
    public async Task LookupByBarcode_WhenProductOutOfStock_ReturnsClearGuidanceMessage()
    {
        var productRepo = new FakeProductRepo();
        var batchRepo = new FakeBatchRepo();
        var clock = new FakeClock();
        var service = new ProductLookupService(productRepo, batchRepo, clock);

        var product = new Product
        {
            Id = 10,
            Name = "Panadol 500mg",
            ProductCode = "PAN-500",
            Barcode = "1234567890123",
            DefaultPurchasePrice = 20m,
            DefaultSalePrice = 30m,
            IsActive = true
        };
        await productRepo.AddAsync(product);

        var result = await service.LookupByBarcodeOrCodeAsync("1234567890123");

        Assert.Equal(ProductLookupStatus.OutOfStock, result.Status);
        Assert.Contains("exists but is currently out of stock", result.Message);
        Assert.Contains("Add stock through Purchase Entry before selling", result.Message);
        Assert.Equal(0m, result.TotalSaleableQuantity);
    }

    [Fact]
    public async Task SearchProductsAsync_WhenMultipleMatches_ReturnsAllEvaluatedProducts()
    {
        var productRepo = new FakeProductRepo();
        var batchRepo = new FakeBatchRepo();
        var clock = new FakeClock();
        var service = new ProductLookupService(productRepo, batchRepo, clock);

        await productRepo.AddAsync(new Product
        {
            Name = "Panadol 500mg Tab",
            GenericName = "Paracetamol",
            ProductCode = "MED-01",
            Barcode = "1111",
            DefaultSalePrice = 10m,
            IsActive = true
        });
        await productRepo.AddAsync(new Product
        {
            Name = "Panadol Extra",
            GenericName = "Paracetamol + Caffeine",
            ProductCode = "MED-02",
            Barcode = "2222",
            DefaultSalePrice = 15m,
            IsActive = true
        });

        var results = await service.SearchProductsAsync("Panadol");

        Assert.Equal(2, results.Count);
        Assert.Equal("Panadol 500mg Tab", results[0].Product?.Name);
        Assert.Equal("Panadol Extra", results[1].Product?.Name);
        Assert.Equal(ProductLookupStatus.OutOfStock, results[0].Status);
        Assert.Equal(ProductLookupStatus.OutOfStock, results[1].Status);
    }

    [Fact]
    public void LookupDto_ToString_ReturnsName()
    {
        var dto = new LookupDto(5, "Tablets", "Tab");
        Assert.Equal("Tablets", dto.ToString());
    }
}
