using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class SaleInvoiceRepository : ISaleInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<SaleInvoiceRepository> _logger;

    public SaleInvoiceRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<SaleInvoiceRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<SaleInvoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleInvoices
            .Include(s => s.Customer)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Unit)
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
                    .ThenInclude(b => b.ProductBatch)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<SaleInvoice?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleInvoices
            .Include(s => s.Customer)
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OperationId == operationId, cancellationToken);
    }

    public async Task<SaleInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SaleInvoices
            .Include(s => s.Customer)
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber, cancellationToken);
    }

    public async Task<List<SaleInvoiceDto>> GetInvoicesAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleType? saleType,
        SaleInvoiceStatus? status,
        string? searchTerm,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.SaleInvoices
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(s => s.BusinessDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(s => s.BusinessDate <= toDate.Value);

        if (customerId.HasValue)
            query = query.Where(s => s.CustomerId == customerId.Value);

        if (saleType.HasValue)
            query = query.Where(s => s.SaleType == saleType.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            string term = searchTerm.Trim();
            query = query.Where(s => s.InvoiceNumber.Contains(term) ||
                                     s.CustomerNameSnapshot.Contains(term) ||
                                     (s.Reference != null && s.Reference.Contains(term)));
        }

        var list = await query
            .OrderByDescending(s => s.Id)
            .Take(take)
            .Select(s => new SaleInvoiceDto
            {
                Id = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                OperationId = s.OperationId,
                InvoiceDateTimeUtc = s.InvoiceDateTimeUtc,
                BusinessDate = s.BusinessDate,
                CustomerId = s.CustomerId,
                CustomerNameSnapshot = s.CustomerNameSnapshot,
                SaleType = s.SaleType,
                Reference = s.Reference,
                Remarks = s.Remarks,
                GrossTotal = s.GrossTotal,
                LineDiscountTotal = s.LineDiscountTotal,
                InvoiceDiscountAmount = s.InvoiceDiscountAmount,
                TaxAmount = s.TaxAmount,
                NetTotal = s.NetTotal,
                TenderedAmount = s.TenderedAmount,
                ChangeGiven = s.ChangeGiven,
                Status = s.Status,
                PostedAtUtc = s.PostedAtUtc,
                CancelledAtUtc = s.CancelledAtUtc,
                CancellationReason = s.CancellationReason,
                PharmacyNameSnapshot = s.PharmacyNameSnapshot,
                PharmacyAddressSnapshot = s.PharmacyAddressSnapshot,
                PharmacyPhoneSnapshot = s.PharmacyPhoneSnapshot,
                DrugLicenseNoSnapshot = s.DrugLicenseNoSnapshot,
                TaxNumberSnapshot = s.TaxNumberSnapshot,
                ReceiptFooterSnapshot = s.ReceiptFooterSnapshot
            })
            .ToListAsync(cancellationToken);

        return list;
    }
}

