using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class ProductBatchRepository : IProductBatchRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ProductBatchRepository> _logger;

    public ProductBatchRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ProductBatchRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ProductBatch?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<ProductBatch?> GetByNormalizedAsync(
        int productId,
        string normalizedBatchNumber,
        DateOnly expiryDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.ProductId == productId &&
                b.NormalizedBatchNumber == normalizedBatchNumber &&
                b.ExpiryDate == expiryDate,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var batches = await context.ProductBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .Where(b => b.ProductId == productId && b.IsActive && b.QuantityOnHand > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(cancellationToken);

        return batches.Select(b =>
        {
            var daysUntil = b.ExpiryDate.DayNumber - today.DayNumber;
            return new ProductBatchDto(
                b.Id,
                b.ProductId,
                b.Product.Name,
                b.BatchNumber,
                b.NormalizedBatchNumber,
                b.ManufacturingDate,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.InventoryValue,
                b.AveragePurchaseCost,
                b.LastPurchaseCost,
                b.SuggestedSalePrice,
                b.IsActive,
                daysUntil < 0,
                daysUntil >= 0 && daysUntil <= 90,
                daysUntil,
                b.RowVersion);
        }).ToList();
    }

    public async Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        int? productId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var queryable = context.ProductBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .AsQueryable();

        if (productId.HasValue)
        {
            queryable = queryable.Where(b => b.ProductId == productId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            queryable = queryable.Where(b =>
                b.BatchNumber.Contains(term) ||
                b.Product.Name.Contains(term));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(b => b.Product.Name)
            .ThenBy(b => b.ExpiryDate)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new
            {
                b.Id,
                b.ProductId,
                ProductName = b.Product.Name,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.AveragePurchaseCost,
                b.InventoryValue
            })
            .ToListAsync(cancellationToken);

        var dtos = items.Select(b =>
        {
            string status = b.ExpiryDate < today
                ? "Expired"
                : (b.ExpiryDate <= today.AddDays(90) ? "Near Expiry" : "Active");

            return new BatchStockDto(
                b.Id,
                b.ProductId,
                b.ProductName,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.AveragePurchaseCost,
                b.InventoryValue,
                status);
        }).ToList();

        return new PagedResult<BatchStockDto>(dtos, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        int? categoryId = null,
        int? manufacturerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var queryable = context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Manufacturer)
            .AsQueryable();

        if (categoryId.HasValue)
        {
            queryable = queryable.Where(p => p.CategoryId == categoryId.Value);
        }

        if (manufacturerId.HasValue)
        {
            queryable = queryable.Where(p => p.ManufacturerId == manufacturerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            queryable = queryable.Where(p =>
                p.Name.Contains(term) ||
                (p.ProductCode != null && p.ProductCode.Contains(term)) ||
                (p.GenericName != null && p.GenericName.Contains(term)));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(p => p.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new CurrentStockDto(
                p.Id,
                p.Name,
                p.ProductCode,
                p.Manufacturer != null ? p.Manufacturer.Name : null,
                p.Category != null ? p.Category.Name : null,
                p.Batches.Sum(b => (decimal?)b.QuantityOnHand) ?? 0m,
                p.Batches.Sum(b => (decimal?)b.InventoryValue) ?? 0m))
            .ToListAsync(cancellationToken);

        return new PagedResult<CurrentStockDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(
        PaginationQuery query,
        int nearExpiryDaysThreshold = 90,
        string? filterStatus = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nearExpiryLimit = today.AddDays(nearExpiryDaysThreshold);

        var queryable = context.ProductBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .Where(b => b.QuantityOnHand > 0);

        if (string.Equals(filterStatus, "Expired", StringComparison.OrdinalIgnoreCase))
        {
            queryable = queryable.Where(b => b.ExpiryDate < today);
        }
        else if (string.Equals(filterStatus, "Near Expiry", StringComparison.OrdinalIgnoreCase))
        {
            queryable = queryable.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= nearExpiryLimit);
        }
        else if (string.Equals(filterStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            queryable = queryable.Where(b => b.ExpiryDate > nearExpiryLimit);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var rawItems = await queryable
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Product.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new
            {
                b.ProductId,
                ProductName = b.Product.Name,
                BatchId = b.Id,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.AveragePurchaseCost,
                b.InventoryValue
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(b =>
        {
            int daysUntil = b.ExpiryDate.DayNumber - today.DayNumber;
            string status = daysUntil < 0
                ? "Expired"
                : (daysUntil <= nearExpiryDaysThreshold ? "Near Expiry" : "Active");

            return new ExpiryReportItemDto(
                b.ProductId,
                b.ProductName,
                b.BatchId,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.AveragePurchaseCost,
                b.InventoryValue,
                daysUntil,
                status);
        }).ToList();

        return new PagedResult<ExpiryReportItemDto>(items, totalCount, query.PageNumber, query.PageSize);
    }
}

