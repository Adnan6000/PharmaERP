using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class SaleReturnRepository : ISaleReturnRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<SaleReturnRepository> _logger;

    public SaleReturnRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<SaleReturnRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<SaleReturn?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.OriginalSaleInvoiceItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
                    .ThenInclude(b => b.OriginalSaleInvoiceBatchAllocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<SaleReturn?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.OriginalSaleInvoiceItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
                    .ThenInclude(b => b.OriginalSaleInvoiceBatchAllocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperationId == operationId, cancellationToken);
    }

    public async Task<SaleReturn?> GetByReturnNumberAsync(string returnNumber, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.OriginalSaleInvoiceItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
                    .ThenInclude(b => b.OriginalSaleInvoiceBatchAllocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReturnNumber == returnNumber, cancellationToken);
    }

    public async Task<List<SaleReturnDto>> GetReturnsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleReturnStatus? status,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(r => r.ReturnDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.ReturnDate <= toDate.Value);

        if (customerId.HasValue)
            query = query.Where(r => r.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var list = await query
            .OrderByDescending(r => r.Id)
            .Take(take)
            .Select(r => new SaleReturnDto
            {
                Id = r.Id,
                ReturnNumber = r.ReturnNumber,
                OperationId = r.OperationId,
                OriginalSaleInvoiceId = r.OriginalSaleInvoiceId,
                OriginalSaleInvoiceNumber = r.OriginalSaleInvoice.InvoiceNumber,
                CustomerId = r.CustomerId,
                CustomerNameSnapshot = r.CustomerNameSnapshot,
                ReturnDate = r.ReturnDate,
                Reason = r.Reason,
                TotalAmount = r.TotalAmount,
                Status = r.Status,
                PostedAtUtc = r.PostedAtUtc,
                CancelledAtUtc = r.CancelledAtUtc,
                CancellationReason = r.CancellationReason
            })
            .ToListAsync(cancellationToken);

        return list;
    }
}

