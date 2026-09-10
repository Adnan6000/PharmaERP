using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface IProductBatchRepository
{
    Task<ProductBatch?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ProductBatch?> GetByNormalizedAsync(int productId, string normalizedBatchNumber, DateOnly expiryDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default);

    Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalInventoryValuationAsync(CancellationToken cancellationToken = default);
}

