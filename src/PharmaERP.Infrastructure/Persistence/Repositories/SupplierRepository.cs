using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<SupplierRepository> _logger;

    public SupplierRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<SupplierRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<SupplierDto>> GetPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = context.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            queryable = queryable.Where(s =>
                s.Name.Contains(term) ||
                s.SupplierCode.Contains(term) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.ContactPerson != null && s.ContactPerson.Contains(term)));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(s => s.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.SupplierCode,
                s.Name,
                s.ContactPerson,
                s.Phone,
                s.Address,
                s.IsActive,
                s.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Supplier> AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Suppliers.AddAsync(supplier, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return supplier;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while adding Supplier '{Name}'.", supplier.Name);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Suppliers.FindAsync([supplier.Id], cancellationToken)
                ?? throw new InvalidOperationException($"Supplier with ID {supplier.Id} was not found.");

            context.Entry(existing).Property(s => s.RowVersion).OriginalValue = supplier.RowVersion;

            existing.SupplierCode = supplier.SupplierCode;
            existing.Name = supplier.Name;
            existing.ContactPerson = supplier.ContactPerson;
            existing.Phone = supplier.Phone;
            existing.Address = supplier.Address;
            existing.IsActive = supplier.IsActive;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating Supplier ID {Id}.", supplier.Id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Suppliers.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return false;
            }

            if (rowVersion.Length > 0)
            {
                context.Entry(existing).Property(s => s.RowVersion).OriginalValue = rowVersion;
            }

            existing.IsActive = !existing.IsActive;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while toggling active status on Supplier ID {Id}.", id);
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
        var query = context.Suppliers.AsNoTracking().Where(s => s.SupplierCode == code.Trim());
        if (excludeId.HasValue)
        {
            query = query.Where(s => s.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new LookupDto(s.Id, s.Name, s.SupplierCode))
            .ToListAsync(cancellationToken);
    }
}

