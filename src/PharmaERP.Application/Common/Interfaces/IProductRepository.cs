using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface IProductRepository
{
    Task<PagedResult<ProductDto>> GetPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        int? categoryId = null,
        int? manufacturerId = null,
        CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default);

    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<bool> ExistsBarcodeAsync(string barcode, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    Task<Product?> GetByProductCodeAsync(string productCode, CancellationToken cancellationToken = default);

    Task<List<Product>> SearchProductsAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default);
}

