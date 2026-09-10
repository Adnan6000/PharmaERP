using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Common.Interfaces;

public interface IJournalRepository
{
    Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<JournalEntry?> GetBySourceAsync(JournalSourceDocumentType type, int sourceId, JournalPostingRole role, CancellationToken cancellationToken = default);
    Task<bool> HasJournalForSourceAsync(JournalSourceDocumentType type, int sourceId, JournalPostingRole role, CancellationToken cancellationToken = default);
    Task<LedgerReportDto> GetGeneralLedgerAsync(int accountId, DateOnly fromDate, DateOnly toDate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<LedgerReportDto> GetCustomerLedgerAsync(int customerId, int arControlAccountId, DateOnly fromDate, DateOnly toDate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<LedgerReportDto> GetSupplierLedgerAsync(int supplierId, int apControlAccountId, DateOnly fromDate, DateOnly toDate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal> GetPartyRunningBalanceAsync(int partyId, bool isCustomer, int controlAccountId, CancellationToken cancellationToken = default);
    Task<TrialBalanceReportDto> GetTrialBalanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<DayBookReportDto> GetDayBookAsync(DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<decimal> GetAccountBalanceAsync(int accountId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalSubledgerBalanceAsync(int controlAccountId, bool isCustomer, CancellationToken cancellationToken = default);
    Task<ProfitAndLossStatementDto> GetProfitAndLossAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<BalanceSheetStatementDto> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
}
