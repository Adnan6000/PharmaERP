using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class ManufacturerRepository : IManufacturerRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ManufacturerRepository> _logger;

    public ManufacturerRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ManufacturerRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Manufacturers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(m =>
                m.Name.Contains(term) ||
                (m.ContactPerson != null && m.ContactPerson.Contains(term)) ||
                (m.Phone != null && m.Phone.Contains(term)) ||
                (m.Email != null && m.Email.Contains(term)));
        }

        return await query
            .OrderBy(m => m.Name)
            .Select(m => new ManufacturerDto(m.Id, m.Name, m.ContactPerson, m.Phone, m.Email, m.IsActive, m.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<Manufacturer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Manufacturers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<Manufacturer> AddAsync(Manufacturer manufacturer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manufacturer);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Manufacturers.AddAsync(manufacturer, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return manufacturer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding Manufacturer '{Name}'.", manufacturer.Name);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task UpdateAsync(Manufacturer manufacturer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manufacturer);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Manufacturers.FindAsync([manufacturer.Id], cancellationToken)
                ?? throw new InvalidOperationException($"Manufacturer with ID {manufacturer.Id} was not found.");

            context.Entry(existing).Property(m => m.RowVersion).OriginalValue = manufacturer.RowVersion;

            existing.Name = manufacturer.Name;
            existing.ContactPerson = manufacturer.ContactPerson;
            existing.Phone = manufacturer.Phone;
            existing.Email = manufacturer.Email;
            existing.IsActive = manufacturer.IsActive;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Manufacturer ID {Id}.", manufacturer.Id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Manufacturers.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return false;
            }

            if (rowVersion.Length > 0)
            {
                context.Entry(existing).Property(m => m.RowVersion).OriginalValue = rowVersion;
            }

            existing.IsActive = !existing.IsActive;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling active status on Manufacturer ID {Id}.", id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Manufacturers.AsNoTracking().Where(m => m.Name == name);
        if (excludeId.HasValue)
        {
            query = query.Where(m => m.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Manufacturers
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new LookupDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);
    }
}
