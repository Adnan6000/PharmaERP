using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IStockMovementRepository
{
    Task<PagedResult<StockMovementDto>> GetPagedAsync(PaginationQuery query, int? productId = null, int? batchId = null, CancellationToken cancellationToken = default);

    Task<bool> HasDownstreamOutflowsAsync(int batchId, DateTime afterUtc, CancellationToken cancellationToken = default);
}

