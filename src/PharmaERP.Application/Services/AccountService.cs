using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IAppConfigRepository _appConfigRepository;

    public AccountService(
        IAccountRepository accountRepository,
        IAppConfigRepository appConfigRepository)
    {
        _accountRepository = accountRepository;
        _appConfigRepository = appConfigRepository;
    }

    public async Task<List<AccountDto>> GetAllAccountsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepository.GetAllAsync(activeOnly, cancellationToken);
        return accounts.Select(MapToDto).ToList();
    }

    public async Task<List<AccountTreeDto>> GetAccountTreeAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepository.GetAllAsync(activeOnly: false, cancellationToken);
        var dtos = accounts.Select(a => new AccountTreeDto
        {
            Id = a.Id,
            AccountCode = a.AccountCode,
            Name = a.Name,
            AccountType = a.AccountType,
            ParentAccountId = a.ParentAccountId,
            ParentAccountName = a.ParentAccount?.Name,
            AllowPosting = a.AllowPosting,
            IsControlAccount = a.IsControlAccount,
            IsCashAccount = a.IsCashAccount,
            IsBankAccount = a.IsBankAccount,
            SystemAccountType = a.SystemAccountType,
            IsActive = a.IsActive,
            Description = a.Description
        }).ToList();

        var lookup = dtos.ToDictionary(a => a.Id);
        var roots = new List<AccountTreeDto>();

        foreach (var item in dtos)
        {
            if (item.ParentAccountId.HasValue && lookup.TryGetValue(item.ParentAccountId.Value, out var parent))
            {
                parent.Children.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }

        return roots.OrderBy(r => r.AccountCode).ToList();
    }

    public async Task<AccountDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Account with ID {id} was not found.");
        return MapToDto(account);
    }

    public async Task<AccountDto> GetByCodeAsync(string accountCode, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByCodeAsync(accountCode.Trim(), cancellationToken)
            ?? throw new NotFoundException($"Account with code '{accountCode}' was not found.");
        return MapToDto(account);
    }

    public async Task<AccountDto?> GetBySystemTypeAsync(SystemAccountType systemType, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetBySystemTypeAsync(systemType, cancellationToken);
        return account != null ? MapToDto(account) : null;
    }

    public async Task<AccountDto> CreateAccountAsync(AccountCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.AccountCode))
            throw new ValidationException("Account code is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ValidationException("Account name is required.");

        if (await _accountRepository.ExistsCodeAsync(dto.AccountCode.Trim(), cancellationToken: cancellationToken))
            throw new ValidationException($"Account code '{dto.AccountCode.Trim()}' already exists.");

        if (dto.ParentAccountId.HasValue)
        {
            var parent = await _accountRepository.GetByIdAsync(dto.ParentAccountId.Value, cancellationToken)
                ?? throw new NotFoundException($"Parent account with ID {dto.ParentAccountId.Value} was not found.");

            if (parent.AccountType != dto.AccountType)
                throw new ValidationException("Child account must have the same AccountType as its parent.");
        }

        // Semantic consistency rules
        bool isControl = dto.IsControlAccount;
        bool isCash = dto.IsCashAccount;
        if (dto.SystemAccountType.HasValue)
        {
            if (dto.SystemAccountType == SystemAccountType.AccountsReceivableControl ||
                dto.SystemAccountType == SystemAccountType.AccountsPayableControl)
            {
                isControl = true;
            }
            if (dto.SystemAccountType == SystemAccountType.CashOnHand)
            {
                isCash = true;
            }
        }

        var account = new Account
        {
            AccountCode = dto.AccountCode.Trim(),
            Name = dto.Name.Trim(),
            AccountType = dto.AccountType,
            ParentAccountId = dto.ParentAccountId,
            AllowPosting = dto.AllowPosting,
            IsControlAccount = isControl,
            IsCashAccount = isCash,
            IsBankAccount = dto.IsBankAccount,
            SystemAccountType = dto.SystemAccountType,
            IsActive = true,
            Description = dto.Description?.Trim()
        };

        var created = await _accountRepository.AddAsync(account, cancellationToken);
        return MapToDto(created);
    }

    public async Task<AccountDto> UpdateAccountAsync(int id, AccountUpdateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var account = await _accountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Account with ID {id} was not found.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ValidationException("Account name is required.");

        if (dto.ParentAccountId.HasValue)
        {
            if (dto.ParentAccountId.Value == id)
                throw new ValidationException("An account cannot be its own parent.");

            if (await _accountRepository.HasDescendantAsync(id, dto.ParentAccountId.Value, cancellationToken))
                throw new ValidationException("Hierarchy cycle detected: an account cannot have one of its descendants as parent.");
        }

        // System account semantic immutability: if account has posted history, cannot deactivate
        if (!dto.IsActive && account.IsActive)
        {
            if (account.SystemAccountType.HasValue)
            {
                string? rawState = await _appConfigRepository.GetValueAsync("Accounting.SetupState", cancellationToken);
                if (rawState == AccountingSetupState.Active.ToString())
                {
                    throw new ValidationException($"Cannot deactivate required system account '{account.Name}' while accounting is active.");
                }
            }
        }

        account.Name = dto.Name.Trim();
        account.ParentAccountId = dto.ParentAccountId;
        account.AllowPosting = dto.AllowPosting;
        account.IsControlAccount = dto.IsControlAccount;
        account.IsCashAccount = dto.IsCashAccount;
        account.IsBankAccount = dto.IsBankAccount;
        account.IsActive = dto.IsActive;
        account.Description = dto.Description?.Trim();

        await _accountRepository.UpdateAsync(account, cancellationToken);
        return MapToDto(account);
    }

    public async Task<bool> DeactivateAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Account with ID {id} was not found.");

        if (account.SystemAccountType.HasValue)
        {
            string? rawState = await _appConfigRepository.GetValueAsync("Accounting.SetupState", cancellationToken);
            if (rawState == AccountingSetupState.Active.ToString())
            {
                throw new ValidationException($"Cannot deactivate required system account '{account.Name}' while accounting is active.");
            }
        }

        account.IsActive = false;
        await _accountRepository.UpdateAsync(account, cancellationToken);
        return true;
    }

    private static AccountDto MapToDto(Account a) => new()
    {
        Id = a.Id,
        AccountCode = a.AccountCode,
        Name = a.Name,
        AccountType = a.AccountType,
        ParentAccountId = a.ParentAccountId,
        ParentAccountName = a.ParentAccount?.Name,
        AllowPosting = a.AllowPosting,
        IsControlAccount = a.IsControlAccount,
        IsCashAccount = a.IsCashAccount,
        IsBankAccount = a.IsBankAccount,
        SystemAccountType = a.SystemAccountType,
        IsActive = a.IsActive,
        Description = a.Description
    };
}
