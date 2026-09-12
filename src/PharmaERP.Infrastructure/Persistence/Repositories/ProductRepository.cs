using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ProductRepository> _logger;

    public ProductRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ProductRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<ProductDto>> GetPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        int? categoryId = null,
        int? manufacturerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var queryable = context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            queryable = queryable.Where(p =>
                p.Name.Contains(term) ||
                (p.GenericName != null && p.GenericName.Contains(term)) ||
                (p.ProductCode != null && p.ProductCode.Contains(term)) ||
                (p.Barcode != null && p.Barcode.Contains(term)));
        }

        if (categoryId.HasValue)
        {
            queryable = queryable.Where(p => p.CategoryId == categoryId.Value);
        }

        if (manufacturerId.HasValue)
        {
            queryable = queryable.Where(p => p.ManufacturerId == manufacturerId.Value);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(p => p.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.GenericName,
                p.ProductCode,
                p.Barcode,
                p.DefaultPurchasePrice,
                p.DefaultSalePrice,
                p.IsActive,
                p.CategoryId,
                p.Category != null ? p.Category.Name : null,
                p.ManufacturerId,
                p.Manufacturer != null ? p.Manufacturer.Name : null,
                p.UnitId,
                p.Unit != null ? p.Unit.Abbreviation : null,
                p.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Manufacturer)
            .Include(p => p.Unit)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Products.AddAsync(product, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while adding Product '{Name}'.", product.Name);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Products.FindAsync([product.Id], cancellationToken)
                ?? throw new InvalidOperationException($"Product with ID {product.Id} was not found.");

            context.Entry(existing).Property(p => p.RowVersion).OriginalValue = product.RowVersion;

            existing.Name = product.Name;
            existing.GenericName = product.GenericName;
            existing.ProductCode = product.ProductCode;
            existing.Barcode = product.Barcode;
            existing.DefaultPurchasePrice = product.DefaultPurchasePrice;
            existing.DefaultSalePrice = product.DefaultSalePrice;
            existing.IsActive = product.IsActive;
            existing.CategoryId = product.CategoryId;
            existing.ManufacturerId = product.ManufacturerId;
            existing.UnitId = product.UnitId;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating Product ID {Id}.", product.Id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Products.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return false;
            }

            if (rowVersion.Length > 0)
            {
                context.Entry(existing).Property(p => p.RowVersion).OriginalValue = rowVersion;
            }

            existing.IsActive = !existing.IsActive;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while toggling active status on Product ID {Id}.", id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products.AsNoTracking().Where(p => p.ProductCode == code.Trim());
        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> ExistsBarcodeAsync(string barcode, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products.AsNoTracking().Where(p => p.Barcode == barcode.Trim());
        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Products
            .Include(p => p.Unit)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Barcode == barcode.Trim(), cancellationToken);
    }

    public async Task<Product?> GetByProductCodeAsync(string productCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productCode)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Products
            .Include(p => p.Unit)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductCode == productCode.Trim(), cancellationToken);
    }

    public async Task<List<Product>> SearchProductsAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<Product>();
        string term = query.Trim();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Products
            .Include(p => p.Unit)
            .AsNoTracking()
            .Where(p => p.Name.Contains(term) ||
                        (p.GenericName != null && p.GenericName.Contains(term)) ||
                        (p.ProductCode != null && p.ProductCode.Contains(term)) ||
                        (p.Barcode != null && p.Barcode.Contains(term)))
            .OrderBy(p => p.Name)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }
}

