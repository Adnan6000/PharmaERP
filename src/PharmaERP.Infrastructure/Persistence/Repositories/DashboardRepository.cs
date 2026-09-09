using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IBusinessClock _businessClock;

    public DashboardRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IBusinessClock businessClock)
    {
        _contextFactory = contextFactory;
        _businessClock = businessClock;
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalProducts = await context.Products.CountAsync(cancellationToken);
        var activeProducts = await context.Products.CountAsync(p => p.IsActive, cancellationToken);
        var totalManufacturers = await context.Manufacturers.CountAsync(cancellationToken);
        var totalCategories = await context.Categories.CountAsync(cancellationToken);
        var totalUnits = await context.Units.CountAsync(cancellationToken);

        var today = await _businessClock.GetBusinessDateAsync(cancellationToken);

        var todaySalesQuery = context.SaleInvoices
            .Where(s => s.BusinessDate == today && s.Status == SaleInvoiceStatus.Posted);

        var todaySalesTotal = await todaySalesQuery.SumAsync(s => (decimal?)s.NetTotal, cancellationToken) ?? 0m;
        var todayInvoiceCount = await todaySalesQuery.CountAsync(cancellationToken);

        var todayReturnsQuery = context.SaleReturns
            .Where(r => r.ReturnDate == today && r.Status == SaleReturnStatus.Posted);

        var todayReturnsTotal = await todayReturnsQuery.SumAsync(r => (decimal?)r.TotalAmount, cancellationToken) ?? 0m;
        var todayReturnCount = await todayReturnsQuery.CountAsync(cancellationToken);

        var todayNetSales = todaySalesTotal - todayReturnsTotal;

        return new DashboardMetricsDto(
            totalProducts,
            activeProducts,
            totalManufacturers,
            totalCategories,
            totalUnits,
            todaySalesTotal,
            todayInvoiceCount,
            todayReturnsTotal,
            todayReturnCount,
            todayNetSales);
    }
}

