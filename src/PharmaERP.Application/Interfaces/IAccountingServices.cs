using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Interfaces;

public interface IAccountService
{
    Task<List<AccountDto>> GetAllAccountsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<List<AccountTreeDto>> GetAccountTreeAsync(CancellationToken cancellationToken = default);
    Task<AccountDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AccountDto> GetByCodeAsync(string accountCode, CancellationToken cancellationToken = default);
    Task<AccountDto?> GetBySystemTypeAsync(SystemAccountType systemType, CancellationToken cancellationToken = default);
    Task<AccountDto> CreateAccountAsync(AccountCreateDto dto, CancellationToken cancellationToken = default);
    Task<AccountDto> UpdateAccountAsync(int id, AccountUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAccountAsync(int id, CancellationToken cancellationToken = default);
}

public interface IAccountingConfigService
{
    Task<AccountingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<DateOnly?> GetAccountingLockDateAsync(CancellationToken cancellationToken = default);
    Task SetAccountingLockDateAsync(DateOnly? lockDate, CancellationToken cancellationToken = default);
    Task<int> GetRequiredSystemAccountIdAsync(SystemAccountType type, CancellationToken cancellationToken = default);
}

public interface IVoucherService
{
    Task<ReceiptVoucherDto> CreateReceiptVoucherAsync(ReceiptVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<ReceiptVoucherDto> CancelReceiptVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<ReceiptVoucherDto> GetReceiptVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<PaymentVoucherDto> CreatePaymentVoucherAsync(PaymentVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<PaymentVoucherDto> CancelPaymentVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<PaymentVoucherDto> GetPaymentVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<JournalVoucherDto> CreateJournalVoucherAsync(JournalVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<JournalVoucherDto> CancelJournalVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<JournalVoucherDto> GetJournalVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<OpeningBalanceVoucherDto> CreateOpeningBalanceVoucherAsync(OpeningBalanceVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<OpeningBalanceVoucherDto> CancelOpeningBalanceVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<OpeningBalanceVoucherDto> GetOpeningBalanceVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ContraVoucherDto> CreateContraVoucherAsync(ContraVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<ContraVoucherDto> CancelContraVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<ContraVoucherDto> GetContraVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<List<ReceiptVoucherDto>> GetReceiptVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<PaymentVoucherDto>> GetPaymentVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<JournalVoucherDto>> GetJournalVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<OpeningBalanceVoucherDto>> GetOpeningBalanceVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<ContraVoucherDto>> GetContraVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
}

public interface IJournalService
{
    Task<LedgerReportDto> GetGeneralLedgerAsync(int accountId, DateOnly fromDate, DateOnly toDate, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<TrialBalanceReportDto> GetTrialBalanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<DayBookReportDto> GetDayBookAsync(DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<JournalEntryDto> GetJournalEntryByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProfitAndLossStatementDto> GetProfitAndLossAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<BalanceSheetStatementDto> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
}

public interface IPaymentSettlementService
{
    Task<ReceiptVoucherAllocationDto> AllocateReceiptVoucherAsync(ReceiptAllocationCreateDto dto, CancellationToken cancellationToken = default);
    Task<PaymentVoucherAllocationDto> AllocatePaymentVoucherAsync(PaymentAllocationCreateDto dto, CancellationToken cancellationToken = default);
    Task<List<OpenSaleInvoiceDto>> GetCustomerOpenInvoicesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<List<OpenPurchaseInvoiceDto>> GetSupplierOpenInvoicesAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<List<ReceiptVoucherAllocationDto>> GetReceiptVoucherAllocationsAsync(int receiptVoucherId, CancellationToken cancellationToken = default);
    Task<List<PaymentVoucherAllocationDto>> GetPaymentVoucherAllocationsAsync(int paymentVoucherId, CancellationToken cancellationToken = default);
    Task<ReceiptVoucherAllocationDto> VoidReceiptAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default);
    Task<PaymentVoucherAllocationDto> VoidPaymentAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default);
}


public interface IPartyLedgerService
{
    Task<LedgerReportDto> GetCustomerLedgerAsync(int customerId, DateOnly fromDate, DateOnly toDate, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<LedgerReportDto> GetSupplierLedgerAsync(int supplierId, DateOnly fromDate, DateOnly toDate, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<decimal> GetCustomerBalanceAsync(int customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetSupplierBalanceAsync(int supplierId, CancellationToken cancellationToken = default);
}

public interface IAccountingReconciliationService
{
    Task<ReconciliationReportDto> RunReconciliationAsync(CancellationToken cancellationToken = default);
}

public interface IAccountingSetupService
{
    Task InitializeDefaultChartOfAccountsAsync(CancellationToken cancellationToken = default);
    Task<HistoricalInitializationResultDto> RunHistoricalInitializationAsync(CancellationToken cancellationToken = default);
}

public interface IAccountingOperationalGate
{
    /// <summary>
    /// Acquires a shared lock allowing concurrent financial transactions, or waits if initialization holds exclusive lock.
    /// Returns disposable lock release.
    /// </summary>
    Task<IAsyncDisposable> AcquireSharedOperationalGateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires an exclusive lock blocking all financial transactions while initialization starts/drains.
    /// Returns disposable lock release.
    /// </summary>
    Task<IAsyncDisposable> AcquireExclusiveInitializationGateAsync(CancellationToken cancellationToken = default);
}
