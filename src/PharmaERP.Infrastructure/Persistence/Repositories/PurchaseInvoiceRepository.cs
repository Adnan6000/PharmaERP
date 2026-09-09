using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class PurchaseInvoiceRepository : IPurchaseInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<PurchaseInvoiceRepository> _logger;

    public PurchaseInvoiceRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<PurchaseInvoiceRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<PurchaseInvoiceListDto>> GetPagedAsync(
        PurchaseInvoiceFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = context.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .AsQueryable();

        if (query.SupplierId.HasValue)
        {
            queryable = queryable.Where(i => i.SupplierId == query.SupplierId.Value);
        }

        if (query.FromDate.HasValue)
        {
            queryable = queryable.Where(i => i.InvoiceDate >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            queryable = queryable.Where(i => i.InvoiceDate <= query.ToDate.Value);
        }

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(i => i.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            queryable = queryable.Where(i =>
                i.InvoiceNumber.Contains(term) ||
                (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(term)) ||
                i.Supplier.Name.Contains(term));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new PurchaseInvoiceListDto(
                i.Id,
                i.InvoiceNumber,
                i.SupplierInvoiceNumber,
                i.SupplierId,
                i.Supplier.Name,
                i.InvoiceDate,
                i.NetTotal,
                i.Status,
                i.Items.Count,
                i.PostedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseInvoiceListDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<PurchaseInvoiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .Include(i => i.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var items = entity.Items.Select(item => new PurchaseInvoiceItemDto(
            item.Id,
            item.PurchaseInvoiceId,
            item.ProductId,
            item.Product.Name,
            item.ProductBatchId,
            item.BatchNumber,
            item.ExpiryDate,
            item.ManufacturingDate,
            item.Quantity,
            item.PurchaseRate,
            item.SuggestedSaleRate,
            item.DiscountAmount,
            item.LineTotal,
            item.ReturnedQuantity,
            Math.Max(0m, item.Quantity - item.ReturnedQuantity)
        )).ToList();

        return new PurchaseInvoiceDto(
            entity.Id,
            entity.InvoiceNumber,
            entity.SupplierInvoiceNumber,
            entity.SupplierId,
            entity.Supplier.Name,
            entity.InvoiceDate,
            entity.DueDate,
            entity.Remarks,
            entity.GrossTotal,
            entity.DiscountAmount,
            entity.TaxAmount,
            entity.NetTotal,
            entity.Status,
            entity.PostedAtUtc,
            entity.CancelledAtUtc,
            entity.CancellationReason,
            items,
            entity.RowVersion);
    }

    public async Task<bool> ExistsSupplierInvoiceNumberAsync(
        int supplierId,
        string normalizedNumber,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedNumber))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.PurchaseInvoices
            .AsNoTracking()
            .Where(i => i.SupplierId == supplierId &&
                        i.NormalizedSupplierInvoiceNumber == normalizedNumber &&
                        i.Status != PurchaseInvoiceStatus.Cancelled);

        if (excludeId.HasValue)
        {
            query = query.Where(i => i.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}

