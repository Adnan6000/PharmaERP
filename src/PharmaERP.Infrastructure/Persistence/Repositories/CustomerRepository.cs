using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Infrastructure.Persistence.Helpers;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<CustomerRepository> _logger;

    public CustomerRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<CustomerRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<CustomerDto>> GetPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = context.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            queryable = queryable.Where(c =>
                c.Name.Contains(term) ||
                c.CustomerCode.Contains(term) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(term)));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderBy(c => c.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CustomerDto(
                c.Id,
                c.CustomerCode,
                c.Name,
                c.ContactPerson,
                c.Phone,
                c.Address,
                c.CreditLimit,
                c.IsActive,
                c.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Customers.AddAsync(customer, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while adding Customer '{Name}'.", customer.Name);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Customers.FindAsync([customer.Id], cancellationToken)
                ?? throw new InvalidOperationException($"Customer with ID {customer.Id} was not found.");

            context.Entry(existing).Property(c => c.RowVersion).OriginalValue = customer.RowVersion;

            existing.CustomerCode = customer.CustomerCode;
            existing.Name = customer.Name;
            existing.ContactPerson = customer.ContactPerson;
            existing.Phone = customer.Phone;
            existing.Address = customer.Address;
            existing.CreditLimit = customer.CreditLimit;
            existing.IsActive = customer.IsActive;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating Customer ID {Id}.", customer.Id);
            throw DbExceptionHelper.TranslateException(ex);
        }
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Customers.FindAsync([id], cancellationToken);
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
            _logger.LogError(ex, "Error occurred while toggling active status on Customer ID {Id}.", id);
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
        var query = context.Customers.AsNoTracking().Where(c => c.CustomerCode == code.Trim());
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new LookupDto(c.Id, c.Name, c.CustomerCode))
            .ToListAsync(cancellationToken);
    }
}

