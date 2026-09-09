using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class StockMovementRepository : IStockMovementRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<StockMovementRepository> _logger;

    public StockMovementRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<StockMovementRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<StockMovementDto>> GetPagedAsync(
        PaginationQuery query,
        int? productId = null,
        int? batchId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = context.StockMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.ProductBatch)
            .AsQueryable();

        if (productId.HasValue)
        {
            queryable = queryable.Where(m => m.ProductId == productId.Value);
        }

        if (batchId.HasValue)
        {
            queryable = queryable.Where(m => m.ProductBatchId == batchId.Value);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(m => m.TransactionDateUtc)
            .ThenByDescending(m => m.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new StockMovementDto(
                m.Id,
                m.ProductId,
                m.Product.Name,
                m.ProductBatchId,
                m.ProductBatch.BatchNumber,
                m.MovementType,
                m.QuantityDelta,
                m.BalanceAfter,
                m.UnitCost,
                m.InventoryCostRate,
                m.InventoryValueDelta,
                m.InventoryValueAfter,
                m.ReferenceDocumentType,
                m.ReferenceDocumentId,
                m.ReferenceDocumentItemId,
                m.ReferenceDocumentNumber,
                m.ReversesStockMovementId,
                m.TransactionDateUtc,
                m.Remarks))
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovementDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<bool> HasDownstreamOutflowsAsync(int batchId, DateTime afterUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductBatchId == batchId &&
                        m.TransactionDateUtc >= afterUtc &&
                        m.QuantityDelta < 0)
            .AnyAsync(cancellationToken);
    }
}

