using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class AccountingSetupService : IAccountingSetupService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IAppConfigRepository _appConfigRepository;
    private readonly IAccountingOperationalGate _operationalGate;
    private readonly IAccountingReconciliationService _reconciliationService;
    private readonly IBusinessClock _businessClock;
    private readonly IJournalRepository _journalRepository;
    private readonly IAccountingTransactionWriter _accountingTransactionWriter;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly IHistoricalBackfillRunner _backfillRunner;
    private readonly ILogger<AccountingSetupService> _logger;

    public const string SetupStateKey = "Accounting.SetupState";

    public AccountingSetupService(
        IAccountRepository accountRepository,
        IAppConfigRepository appConfigRepository,
        IAccountingOperationalGate operationalGate,
        IAccountingReconciliationService reconciliationService,
        IBusinessClock businessClock,
        IJournalRepository journalRepository,
        IAccountingTransactionWriter accountingTransactionWriter,
        IDocumentNumberGenerator documentNumberGenerator,
        IHistoricalBackfillRunner backfillRunner,
        ILogger<AccountingSetupService> logger)
    {
        _accountRepository = accountRepository;
        _appConfigRepository = appConfigRepository;
        _operationalGate = operationalGate;
        _reconciliationService = reconciliationService;
        _businessClock = businessClock;
        _journalRepository = journalRepository;
        _accountingTransactionWriter = accountingTransactionWriter;
        _documentNumberGenerator = documentNumberGenerator;
        _backfillRunner = backfillRunner;
        _logger = logger;
    }

    public async Task InitializeDefaultChartOfAccountsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _accountRepository.GetAllAsync(activeOnly: false, cancellationToken);
        if (existing.Count > 0)
        {
            return; // Already initialized or custom COA exists
        }

        // Standard 4-digit hierarchy
        // 1000 - ASSETS
        var assetsHeader = await _accountRepository.AddAsync(new Account
        {
            AccountCode = "1000",
            Name = "Assets",
            AccountType = AccountType.Asset,
            AllowPosting = false,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "1010",
            Name = "Cash on Hand",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsCashAccount = true,
            SystemAccountType = SystemAccountType.CashOnHand,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "1020",
            Name = "Cash at Bank",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsBankAccount = true,
            SystemAccountType = SystemAccountType.DefaultBankAccount,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "1030",
            Name = "Accounts Receivable Control",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.AccountsReceivableControl,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "1040",
            Name = "Merchandise Inventory",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.Inventory,
            IsActive = true
        }, cancellationToken);

        // 2000 - LIABILITIES
        var liabHeader = await _accountRepository.AddAsync(new Account
        {
            AccountCode = "2000",
            Name = "Liabilities",
            AccountType = AccountType.Liability,
            AllowPosting = false,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "2010",
            Name = "Accounts Payable Control",
            AccountType = AccountType.Liability,
            ParentAccountId = liabHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.AccountsPayableControl,
            IsActive = true
        }, cancellationToken);

        // 3000 - EQUITY
        var equityHeader = await _accountRepository.AddAsync(new Account
        {
            AccountCode = "3000",
            Name = "Equity",
            AccountType = AccountType.Equity,
            AllowPosting = false,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "3010",
            Name = "Owner's Capital",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "3020",
            Name = "Retained Earnings",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "3030",
            Name = "Opening Balance Equity",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.OpeningBalanceEquity,
            IsActive = true
        }, cancellationToken);

        // 4000 - REVENUE
        var revHeader = await _accountRepository.AddAsync(new Account
        {
            AccountCode = "4000",
            Name = "Revenue",
            AccountType = AccountType.Revenue,
            AllowPosting = false,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "4010",
            Name = "Sales Revenue",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesRevenue,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "4020",
            Name = "Sales Returns & Allowances",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesReturns,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "4030",
            Name = "Sales Discounts Allowed",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesDiscount,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "4040",
            Name = "Purchase Discounts Received",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.PurchaseDiscount,
            IsActive = true
        }, cancellationToken);

        // 5000 - EXPENSES & COGS
        var expHeader = await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5000",
            Name = "Expenses & Cost of Sales",
            AccountType = AccountType.Expense,
            AllowPosting = false,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5010",
            Name = "Cost of Goods Sold",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.CostOfGoodsSold,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5020",
            Name = "Salaries & Wages",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5030",
            Name = "Rent Expense",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5040",
            Name = "Utilities Expense",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);

        await _accountRepository.AddAsync(new Account
        {
            AccountCode = "5050",
            Name = "General & Administrative Expenses",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            IsActive = true
        }, cancellationToken);
    }

    public async Task<HistoricalInitializationResultDto> RunHistoricalInitializationAsync(CancellationToken cancellationToken = default)
    {
        // 1. Acquire EXCLUSIVE operational gate to drain and block in-flight financial mutations
        await using var exclusiveGate = await _operationalGate.AcquireExclusiveInitializationGateAsync(cancellationToken);

        // 2. Atomic state transition: NotConfigured or InitializationFailed -> Initializing
        string? currentState = await _appConfigRepository.GetValueAsync(SetupStateKey, cancellationToken);
        if (currentState == AccountingSetupState.Initializing.ToString())
        {
            throw new ValidationException("Accounting initialization is already in progress by another session.");
        }
        if (currentState == AccountingSetupState.Active.ToString())
        {
            // Already active; run reconciliation directly
            var recReport = await _reconciliationService.RunReconciliationAsync(cancellationToken);
            return new HistoricalInitializationResultDto
            {
                Success = recReport.AllMatched,
                FinalState = AccountingSetupState.Active,
                ReconciliationReport = recReport
            };
        }

        await _appConfigRepository.SetValueAsync(SetupStateKey, AccountingSetupState.Initializing.ToString(), "Accounting setup state", cancellationToken);

        // 3. Ensure default COA is initialized
        await InitializeDefaultChartOfAccountsAsync(cancellationToken);

        var result = new HistoricalInitializationResultDto
        {
            FinalState = AccountingSetupState.Initializing
        };

        // 4. Delegate to Infrastructure backfill runner
        // (The backfill processing is executed per document in bounded transactions)
        try
        {
            result = await _backfillRunner.ExecuteBackfillAsync(cancellationToken);
            if (result.ErrorMessages.Count > 0)
            {
                await _appConfigRepository.SetValueAsync(SetupStateKey, AccountingSetupState.InitializationFailed.ToString(), "Accounting setup state", cancellationToken);
                return result with { Success = false, FinalState = AccountingSetupState.InitializationFailed };
            }

            var reconciliation = await _reconciliationService.RunReconciliationAsync(cancellationToken);
            result = result with { ReconciliationReport = reconciliation };

            if (reconciliation.AllMatched)
            {
                await _appConfigRepository.SetValueAsync(SetupStateKey, AccountingSetupState.Active.ToString(), "Accounting setup state", cancellationToken);
                return result with { Success = true, FinalState = AccountingSetupState.Active };
            }
            else
            {
                await _appConfigRepository.SetValueAsync(SetupStateKey, AccountingSetupState.InitializationFailed.ToString(), "Accounting setup state", cancellationToken);
                return result with { Success = false, FinalState = AccountingSetupState.InitializationFailed };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Historical accounting initialization failed with unhandled exception.");
            await _appConfigRepository.SetValueAsync(SetupStateKey, AccountingSetupState.InitializationFailed.ToString(), "Accounting setup state", cancellationToken);
            result.ErrorMessages.Add(ex.Message);
            return result with { Success = false, FinalState = AccountingSetupState.InitializationFailed };
        }
    }
}
