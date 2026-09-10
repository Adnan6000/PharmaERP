using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Common.Interfaces;

public interface IAccountRepository
{
    Task<List<Account>> GetAllAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<Account?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Account?> GetByCodeAsync(string accountCode, CancellationToken cancellationToken = default);
    Task<Account?> GetBySystemTypeAsync(SystemAccountType systemType, CancellationToken cancellationToken = default);
    Task<bool> HasPostedJournalLinesAsync(int accountId, CancellationToken cancellationToken = default);
    Task<bool> HasDescendantAsync(int ancestorId, int potentialDescendantId, CancellationToken cancellationToken = default);
    Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default);
    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);
    Task<bool> ExistsCodeAsync(string accountCode, int? excludeId = null, CancellationToken cancellationToken = default);
}
