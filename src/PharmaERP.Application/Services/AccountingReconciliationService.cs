using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class AccountingReconciliationService : IAccountingReconciliationService
{
    private readonly IJournalRepository _journalRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IProductBatchRepository _productBatchRepository;

    public AccountingReconciliationService(
        IJournalRepository journalRepository,
        IAccountRepository accountRepository,
        IProductBatchRepository productBatchRepository)
    {
        _journalRepository = journalRepository;
        _accountRepository = accountRepository;
        _productBatchRepository = productBatchRepository;
    }

    public async Task<ReconciliationReportDto> RunReconciliationAsync(CancellationToken cancellationToken = default)
    {
        var invAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.Inventory, cancellationToken);
        var arAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl, cancellationToken);
        var apAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl, cancellationToken);

        decimal invGlBalance = invAcc != null ? await _journalRepository.GetAccountBalanceAsync(invAcc.Id, cancellationToken) : 0m;
        decimal physicalStockVal = await _productBatchRepository.GetTotalInventoryValuationAsync(cancellationToken);

        decimal arGlBalance = arAcc != null ? await _journalRepository.GetAccountBalanceAsync(arAcc.Id, cancellationToken) : 0m;
        decimal customerTotal = arAcc != null ? await _journalRepository.GetTotalSubledgerBalanceAsync(arAcc.Id, isCustomer: true, cancellationToken) : 0m;

        decimal apGlBalance = apAcc != null ? await _journalRepository.GetAccountBalanceAsync(apAcc.Id, cancellationToken) : 0m;
        decimal supplierTotal = apAcc != null ? await _journalRepository.GetTotalSubledgerBalanceAsync(apAcc.Id, isCustomer: false, cancellationToken) : 0m;

        var tb = await _journalRepository.GetTrialBalanceAsync(DateOnly.MinValue, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(100)), cancellationToken);

        bool invMatch = (invGlBalance - physicalStockVal) == 0m;
        bool arMatch = (arGlBalance - customerTotal) == 0m;
        bool apMatch = (apGlBalance - supplierTotal) == 0m;
        bool tbMatch = (tb.TotalClosingDebit - tb.TotalClosingCredit) == 0m;

        bool allMatched = invMatch && arMatch && apMatch && tbMatch;

        return new ReconciliationReportDto
        {
            AllMatched = allMatched,
            InventoryGlBalance = invGlBalance,
            PhysicalStockValuation = physicalStockVal,
            ArControlBalance = arGlBalance,
            CustomerSubledgersTotal = customerTotal,
            ApControlBalance = apGlBalance,
            SupplierSubledgersTotal = supplierTotal,
            TrialBalanceDebit = tb.TotalClosingDebit,
            TrialBalanceCredit = tb.TotalClosingCredit
        };
    }
}
