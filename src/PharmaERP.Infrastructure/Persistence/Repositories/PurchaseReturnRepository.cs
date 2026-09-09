using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class PurchaseReturnRepository : IPurchaseReturnRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<PurchaseReturnRepository> _logger;

    public PurchaseReturnRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<PurchaseReturnRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<PurchaseReturnListDto>> GetPagedAsync(
        PurchaseReturnFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = context.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.OriginalPurchaseInvoice)
            .Include(r => r.Items)
            .AsQueryable();

        if (query.SupplierId.HasValue)
        {
            queryable = queryable.Where(r => r.SupplierId == query.SupplierId.Value);
        }

        if (query.FromDate.HasValue)
        {
            queryable = queryable.Where(r => r.ReturnDate >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            queryable = queryable.Where(r => r.ReturnDate <= query.ToDate.Value);
        }

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(r => r.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            queryable = queryable.Where(r =>
                r.ReturnNumber.Contains(term) ||
                (r.OriginalPurchaseInvoice != null && r.OriginalPurchaseInvoice.InvoiceNumber.Contains(term)) ||
                r.Supplier.Name.Contains(term));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new PurchaseReturnListDto(
                r.Id,
                r.ReturnNumber,
                r.OriginalPurchaseInvoice != null ? r.OriginalPurchaseInvoice.InvoiceNumber : null,
                r.SupplierId,
                r.Supplier.Name,
                r.ReturnDate,
                r.TotalAmount,
                r.Status,
                r.Items.Count,
                r.PostedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseReturnListDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.OriginalPurchaseInvoice)
            .Include(r => r.Items)
                .ThenInclude(item => item.Product)
            .Include(r => r.Items)
                .ThenInclude(item => item.ProductBatch)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var items = entity.Items.Select(item => new PurchaseReturnItemDto(
            item.Id,
            item.PurchaseReturnId,
            item.OriginalPurchaseInvoiceItemId,
            item.ProductId,
            item.Product.Name,
            item.ProductBatchId,
            item.ProductBatch.BatchNumber,
            item.Quantity,
            item.ReturnRate,
            item.LineTotal
        )).ToList();

        return new PurchaseReturnDto(
            entity.Id,
            entity.ReturnNumber,
            entity.OriginalPurchaseInvoiceId,
            entity.OriginalPurchaseInvoice?.InvoiceNumber,
            entity.SupplierId,
            entity.Supplier.Name,
            entity.ReturnDate,
            entity.Reason,
            entity.TotalAmount,
            entity.Status,
            entity.PostedAtUtc,
            items,
            entity.RowVersion);
    }
}

