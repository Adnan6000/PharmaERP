using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<CategoryRepository> _logger;

    public CategoryRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<CategoryRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Categories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(c =>
                c.Name.Contains(term) ||
                (c.Description != null && c.Description.Contains(term)));
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Category> AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Categories.AddAsync(category, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return category;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding Category '{Name}'.", category.Name);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Categories.FindAsync([category.Id], cancellationToken)
                ?? throw new InvalidOperationException($"Category with ID {category.Id} was not found.");

            context.Entry(existing).Property(c => c.RowVersion).OriginalValue = category.RowVersion;

            existing.Name = category.Name;
            existing.Description = category.Description;
            existing.IsActive = category.IsActive;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Category ID {Id}.", category.Id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Categories.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return false;
            }

            if (rowVersion.Length > 0)
            {
                context.Entry(existing).Property(c => c.RowVersion).OriginalValue = rowVersion;
            }

            existing.IsActive = !existing.IsActive;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling active status on Category ID {Id}.", id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Categories.AsNoTracking().Where(c => c.Name == name.Trim());
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new LookupDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }
}

