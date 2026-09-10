using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class AccountingConfigService : IAccountingConfigService
{
    private readonly IAppConfigRepository _appConfigRepository;
    private readonly IAccountRepository _accountRepository;

    public const string SetupStateKey = "Accounting.SetupState";
    public const string LockDateKey = "Accounting.LockDate";

    public AccountingConfigService(
        IAppConfigRepository appConfigRepository,
        IAccountRepository accountRepository)
    {
        _appConfigRepository = appConfigRepository;
        _accountRepository = accountRepository;
    }

    public async Task<AccountingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? rawState = await _appConfigRepository.GetValueAsync(SetupStateKey, cancellationToken);
        var state = AccountingSetupState.NotConfigured;
        if (!string.IsNullOrWhiteSpace(rawState) && Enum.TryParse<AccountingSetupState>(rawState, out var parsed))
        {
            state = parsed;
        }

        var lockDate = await GetAccountingLockDateAsync(cancellationToken);
        var accounts = await _accountRepository.GetAllAsync(activeOnly: false, cancellationToken);

        return new AccountingStatusDto
        {
            State = state,
            AccountingLockDate = lockDate,
            HasAccounts = accounts.Count > 0,
            HasUnaccountedHistoricalDocuments = false,
            UnaccountedHistoricalCount = 0
        };
    }

    public async Task<DateOnly?> GetAccountingLockDateAsync(CancellationToken cancellationToken = default)
    {
        string? raw = await _appConfigRepository.GetValueAsync(LockDateKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(raw) && DateOnly.TryParse(raw, out var d))
        {
            return d;
        }
        return null;
    }

    public async Task SetAccountingLockDateAsync(DateOnly? lockDate, CancellationToken cancellationToken = default)
    {
        if (lockDate.HasValue)
        {
            await _appConfigRepository.SetValueAsync(LockDateKey, lockDate.Value.ToString("yyyy-MM-dd"), "Accounting period lock date", cancellationToken);
        }
        else
        {
            await _appConfigRepository.SetValueAsync(LockDateKey, string.Empty, "Accounting period lock date", cancellationToken);
        }
    }

    public async Task<int> GetRequiredSystemAccountIdAsync(SystemAccountType type, CancellationToken cancellationToken = default)
    {
        var acc = await _accountRepository.GetBySystemTypeAsync(type, cancellationToken)
            ?? throw new ValidationException($"Required system account mapping for '{type}' is not configured or missing.");

        if (!acc.IsActive)
            throw new ValidationException($"System account '{acc.Name}' ({type}) is deactivated.");

        return acc.Id;
    }
}
