using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class VoucherRepository : IVoucherRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public VoucherRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ReceiptVoucher?> GetReceiptVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ReceiptVouchers
            .Include(v => v.CashOrBankAccount)
            .Include(v => v.OffsetAccount)
            .Include(v => v.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<PaymentVoucher?> GetPaymentVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.PaymentVouchers
            .Include(v => v.CashOrBankAccount)
            .Include(v => v.OffsetAccount)
            .Include(v => v.Supplier)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<JournalVoucher?> GetJournalVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.JournalVouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<OpeningBalanceVoucher?> GetOpeningBalanceVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.OpeningBalanceVouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<ContraVoucher?> GetContraVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ContraVouchers
            .Include(v => v.SourceAccount)
            .Include(v => v.DestinationAccount)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<List<ReceiptVoucher>> GetReceiptVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ReceiptVouchers
            .Include(v => v.CashOrBankAccount)
            .Include(v => v.OffsetAccount)
            .Include(v => v.Customer)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(v => v.BusinessDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.BusinessDate <= toDate.Value);

        return await query.OrderByDescending(v => v.BusinessDate).ThenByDescending(v => v.Id).ToListAsync(cancellationToken);
    }

    public async Task<List<PaymentVoucher>> GetPaymentVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.PaymentVouchers
            .Include(v => v.CashOrBankAccount)
            .Include(v => v.OffsetAccount)
            .Include(v => v.Supplier)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(v => v.BusinessDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.BusinessDate <= toDate.Value);

        return await query.OrderByDescending(v => v.BusinessDate).ThenByDescending(v => v.Id).ToListAsync(cancellationToken);
    }

    public async Task<List<JournalVoucher>> GetJournalVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.JournalVouchers.AsNoTracking().AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(v => v.BusinessDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.BusinessDate <= toDate.Value);

        return await query.OrderByDescending(v => v.BusinessDate).ThenByDescending(v => v.Id).ToListAsync(cancellationToken);
    }

    public async Task<List<OpeningBalanceVoucher>> GetOpeningBalanceVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.OpeningBalanceVouchers.AsNoTracking().AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(v => v.BusinessDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.BusinessDate <= toDate.Value);

        return await query.OrderByDescending(v => v.BusinessDate).ThenByDescending(v => v.Id).ToListAsync(cancellationToken);
    }

    public async Task<List<ContraVoucher>> GetContraVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ContraVouchers
            .Include(v => v.SourceAccount)
            .Include(v => v.DestinationAccount)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(v => v.BusinessDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.BusinessDate <= toDate.Value);

        return await query.OrderByDescending(v => v.BusinessDate).ThenByDescending(v => v.Id).ToListAsync(cancellationToken);
    }
}
