using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetProductsPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ProductDto> CreateProductAsync(ProductUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateProductAsync(ProductUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<ProductLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default);
}

