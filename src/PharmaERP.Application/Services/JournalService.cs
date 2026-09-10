using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;

namespace PharmaERP.Application.Services;

public class JournalService : IJournalService
{
    private readonly IJournalRepository _journalRepository;
    private readonly IAccountRepository _accountRepository;

    public JournalService(
        IJournalRepository journalRepository,
        IAccountRepository accountRepository)
    {
        _journalRepository = journalRepository;
        _accountRepository = accountRepository;
    }

    public async Task<LedgerReportDto> GetGeneralLedgerAsync(
        int accountId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var acc = await _accountRepository.GetByIdAsync(accountId, cancellationToken)
            ?? throw new NotFoundException($"Account with ID {accountId} was not found.");

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 50;

        return await _journalRepository.GetGeneralLedgerAsync(accountId, fromDate, toDate, pageNumber, pageSize, cancellationToken);
    }

    public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
            throw new ValidationException("FromDate cannot be greater than ToDate.");

        return await _journalRepository.GetTrialBalanceAsync(fromDate, toDate, cancellationToken);
    }

    public async Task<DayBookReportDto> GetDayBookAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        return await _journalRepository.GetDayBookAsync(businessDate, cancellationToken);
    }

    public async Task<JournalEntryDto> GetJournalEntryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _journalRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Journal entry with ID {id} was not found.");

        return new JournalEntryDto
        {
            Id = entry.Id,
            EntryNumber = entry.EntryNumber,
            BusinessDate = entry.BusinessDate,
            PostedAtUtc = entry.PostedAtUtc,
            Narration = entry.Narration,
            SourceDocumentType = entry.SourceDocumentType,
            SourceDocumentId = entry.SourceDocumentId,
            SourceDocumentNumber = entry.SourceDocumentNumber,
            PostingRole = entry.PostingRole,
            Status = entry.Status,
            OperationId = entry.OperationId,
            ReversesJournalEntryId = entry.ReversesJournalEntryId,
            TotalDebit = entry.TotalDebit,
            TotalCredit = entry.TotalCredit,
            CancelledAtUtc = entry.CancelledAtUtc,
            CancellationReason = entry.CancellationReason,
            Lines = entry.Lines.Select(l => new JournalEntryLineDto
            {
                Id = l.Id,
                AccountId = l.AccountId,
                AccountCode = l.Account.AccountCode,
                AccountName = l.Account.Name,
                DebitAmount = l.DebitAmount,
                CreditAmount = l.CreditAmount,
                CustomerId = l.CustomerId,
                CustomerName = l.Customer?.Name,
                SupplierId = l.SupplierId,
                SupplierName = l.Supplier?.Name,
                Narration = l.Narration
            }).ToList()
        };
    }

    public async Task<ProfitAndLossStatementDto> GetProfitAndLossAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
            throw new ValidationException("FromDate cannot be greater than ToDate.");

        return await _journalRepository.GetProfitAndLossAsync(fromDate, toDate, cancellationToken);
    }

    public async Task<BalanceSheetStatementDto> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        return await _journalRepository.GetBalanceSheetAsync(asOfDate, cancellationToken);
    }
}
