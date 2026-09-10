using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AccountRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Account>> GetAllAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Accounts
            .Include(a => a.ParentAccount)
            .AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query.OrderBy(a => a.AccountCode).ToListAsync(cancellationToken);
    }

    public async Task<Account?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Accounts
            .Include(a => a.ParentAccount)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Account?> GetByCodeAsync(string accountCode, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Accounts
            .Include(a => a.ParentAccount)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountCode == accountCode, cancellationToken);
    }

    public async Task<Account?> GetBySystemTypeAsync(SystemAccountType systemType, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.SystemAccountType == systemType, cancellationToken);
    }

    public async Task<bool> HasPostedJournalLinesAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.JournalEntryLines
            .AnyAsync(l => l.AccountId == accountId && l.JournalEntry.Status == JournalEntryStatus.Posted, cancellationToken);
    }

    public async Task<bool> HasDescendantAsync(int ancestorId, int potentialDescendantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var all = await context.Accounts.AsNoTracking().Select(a => new { a.Id, a.ParentAccountId }).ToListAsync(cancellationToken);

        int? current = potentialDescendantId;
        var visited = new HashSet<int>();

        while (current.HasValue)
        {
            if (current.Value == ancestorId) return true;
            if (!visited.Add(current.Value)) break; // cycle guard

            var parent = all.FirstOrDefault(a => a.Id == current.Value);
            current = parent?.ParentAccountId;
        }

        return false;
    }

    public async Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Accounts.AddAsync(account, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Accounts.Update(account);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsCodeAsync(string accountCode, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Accounts.Where(a => a.AccountCode == accountCode);
        if (excludeId.HasValue)
        {
            query = query.Where(a => a.Id != excludeId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }
}
