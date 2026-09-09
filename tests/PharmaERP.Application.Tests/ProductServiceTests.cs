using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class ProductServiceTests
{
    private class FakeProductRepository : IProductRepository
    {
        public readonly List<Product> Products = [];

        public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        }

        public Task<PagedResult<ProductDto>> GetPagedAsync(
            PaginationQuery query,
            string? searchTerm = null,
            CancellationToken cancellationToken = default)
        {
            var filtered = Products.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(p => p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var total = filtered.Count();
            var items = filtered
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(p => new ProductDto(
                    p.Id,
                    p.Name,
                    p.GenericName,
                    p.ProductCode,
                    p.Barcode,
                    p.DefaultPurchasePrice,
                    p.DefaultSalePrice,
                    p.IsActive,
                    p.CategoryId,
                    null,
                    p.ManufacturerId,
                    null,
                    p.UnitId,
                    null,
                    p.RowVersion))
                .ToList();

            return Task.FromResult(new PagedResult<ProductDto>(items, total, query.PageNumber, query.PageSize));
        }

        public Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            product.Id = Products.Count + 1;
            Products.Add(product);
            return Task.FromResult(product);
        }

        public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
        {
            var index = Products.FindIndex(p => p.Id == product.Id);
            if (index >= 0)
            {
                Products[index] = product;
            }
            return Task.CompletedTask;
        }

        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
        {
            var p = Products.FirstOrDefault(x => x.Id == id);
            if (p != null)
            {
                p.IsActive = !p.IsActive;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            bool exists = Products.Any(p => p.ProductCode != null &&
                                            p.ProductCode.Equals(code, StringComparison.OrdinalIgnoreCase) &&
                                            (!excludeId.HasValue || p.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<bool> ExistsBarcodeAsync(string barcode, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            bool exists = Products.Any(p => p.Barcode != null &&
                                            p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase) &&
                                            (!excludeId.HasValue || p.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Barcode != null && p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase)));

        public Task<Product?> GetByProductCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.ProductCode != null && p.ProductCode.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task<List<Product>> SearchProductsAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            (p.GenericName != null && p.GenericName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (p.ProductCode != null && p.ProductCode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (p.Barcode != null && p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(maxResults)
                .ToList());
    }

    private class FakeManufacturerRepository : IManufacturerRepository
    {
        public Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManufacturerDto>>([]);
        public Task<Manufacturer?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Manufacturer?>(null);
        public Task<Manufacturer> AddAsync(Manufacturer entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public Task UpdateAsync(Manufacturer entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LookupDto>>([]);
    }

    private class FakeCategoryRepository : ICategoryRepository
    {
        public Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategoryDto>>([]);
        public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Category?>(null);
        public Task<Category> AddAsync(Category entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public Task UpdateAsync(Category entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LookupDto>>([]);
    }

    private class FakeUnitRepository : IUnitRepository
    {
        public Task<IReadOnlyList<UnitDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitDto>>([]);
        public Task<Unit?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Unit?>(null);
        public Task<Unit> AddAsync(Unit entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public Task UpdateAsync(Unit entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsAbbreviationAsync(string abbreviation, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LookupDto>>([]);
    }

    private static ProductService CreateService(FakeProductRepository productRepo)
    {
        return new ProductService(
            productRepo,
            new FakeManufacturerRepository(),
            new FakeCategoryRepository(),
            new FakeUnitRepository());
    }

    [Fact]
    public async Task CreateProductAsync_ThrowsException_WhenNameIsEmpty()
    {
        var repo = new FakeProductRepository();
        var service = CreateService(repo);
        var dto = new ProductUpsertDto { Name = "   ", DefaultPurchasePrice = 10, DefaultSalePrice = 15 };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(dto));
    }

    [Fact]
    public async Task CreateProductAsync_ThrowsException_WhenPriceIsNegative()
    {
        var repo = new FakeProductRepository();
        var service = CreateService(repo);
        var dto = new ProductUpsertDto { Name = "Amoxicillin", DefaultPurchasePrice = -5, DefaultSalePrice = 15 };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(dto));
    }

    [Fact]
    public async Task CreateProductAsync_ThrowsDuplicateKeyException_WhenProductCodeAlreadyExists()
    {
        var repo = new FakeProductRepository();
        repo.Products.Add(new Product { Id = 1, Name = "Existing", ProductCode = "PRD-001" });
        var service = CreateService(repo);

        var dto = new ProductUpsertDto
        {
            Name = "New Product",
            ProductCode = "PRD-001",
            DefaultPurchasePrice = 10,
            DefaultSalePrice = 15
        };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateProductAsync(dto));
    }

    [Fact]
    public async Task CreateProductAsync_ThrowsDuplicateKeyException_WhenBarcodeAlreadyExists()
    {
        var repo = new FakeProductRepository();
        repo.Products.Add(new Product { Id = 1, Name = "Existing", Barcode = "123456789" });
        var service = CreateService(repo);

        var dto = new ProductUpsertDto
        {
            Name = "New Product",
            Barcode = "123456789",
            DefaultPurchasePrice = 10,
            DefaultSalePrice = 15
        };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateProductAsync(dto));
    }

    [Fact]
    public async Task CreateProductAsync_PersistsAndReturnsProductDto_WhenValid()
    {
        var repo = new FakeProductRepository();
        var service = CreateService(repo);
        var dto = new ProductUpsertDto
        {
            Name = "Amoxicillin 500mg",
            ProductCode = "AMX-500",
            DefaultPurchasePrice = 25.5000m,
            DefaultSalePrice = 32.0000m
        };

        var result = await service.CreateProductAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Amoxicillin 500mg", result.Name);
        Assert.Equal("AMX-500", result.ProductCode);
        Assert.Single(repo.Products);
    }

    [Fact]
    public async Task GetProductsPagedAsync_ReturnsPagedItemsCorrectly()
    {
        var repo = new FakeProductRepository();
        var service = CreateService(repo);

        for (int i = 1; i <= 25; i++)
        {
            await repo.AddAsync(new Product { Name = $"Medicine {i:D2}", DefaultPurchasePrice = 10, DefaultSalePrice = 15 });
        }

        var query = new PaginationQuery { PageNumber = 2, PageSize = 10 };

        var result = await service.GetProductsPagedAsync(query);

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task ToggleActiveAsync_TogglesProductStatusSuccessfully()
    {
        var repo = new FakeProductRepository();
        var product = new Product { Id = 1, Name = "Panadol", IsActive = true, RowVersion = [1, 2, 3] };
        repo.Products.Add(product);
        var service = CreateService(repo);

        var result = await service.ToggleActiveAsync(1, [1, 2, 3]);

        Assert.True(result);
        Assert.False(product.IsActive);
    }
}
