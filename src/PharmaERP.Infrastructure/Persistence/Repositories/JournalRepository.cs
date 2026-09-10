using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class JournalRepository : IJournalRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public JournalRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<JournalEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.JournalEntries
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Customer)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Supplier)
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<JournalEntry?> GetBySourceAsync(
        JournalSourceDocumentType type,
        int sourceId,
        JournalPostingRole role,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.JournalEntries
            .Include(j => j.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.SourceDocumentType == type &&
                                      j.SourceDocumentId == sourceId &&
                                      j.PostingRole == role,
                                      cancellationToken);
    }

    public async Task<bool> HasJournalForSourceAsync(
        JournalSourceDocumentType type,
        int sourceId,
        JournalPostingRole role,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.JournalEntries
            .AnyAsync(j => j.SourceDocumentType == type &&
                           j.SourceDocumentId == sourceId &&
                           j.PostingRole == role &&
                           j.Status == JournalEntryStatus.Posted,
                           cancellationToken);
    }

    public async Task<LedgerReportDto> GetGeneralLedgerAsync(
        int accountId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var account = await context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        // 1. Opening balance before fromDate
        decimal openingBalance = await context.JournalEntryLines
            .Where(l => l.AccountId == accountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate < fromDate)
            .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);

        // 2. Count and total debits/credits in period
        var periodQuery = context.JournalEntryLines
            .Where(l => l.AccountId == accountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate >= fromDate &&
                        l.JournalEntry.BusinessDate <= toDate);

        int totalRows = await periodQuery.CountAsync(cancellationToken);
        decimal periodDebit = await periodQuery.SumAsync(l => l.DebitAmount, cancellationToken);
        decimal periodCredit = await periodQuery.SumAsync(l => l.CreditAmount, cancellationToken);

        // 3. Offset running balance for prior pages: sum of prior page items
        int skip = (pageNumber - 1) * pageSize;
        decimal priorPagesNet = 0m;
        if (skip > 0)
        {
            priorPagesNet = await periodQuery
                .OrderBy(l => l.JournalEntry.BusinessDate)
                .ThenBy(l => l.JournalEntry.PostedAtUtc)
                .ThenBy(l => l.JournalEntryId)
                .ThenBy(l => l.Id)
                .Take(skip)
                .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);
        }

        decimal pageOpeningBalance = openingBalance + priorPagesNet;

        // 4. Fetch paged items
        var pageLines = await periodQuery
            .OrderBy(l => l.JournalEntry.BusinessDate)
            .ThenBy(l => l.JournalEntry.PostedAtUtc)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(l => new
            {
                LineId = l.Id,
                l.JournalEntryId,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.BusinessDate,
                l.JournalEntry.PostedAtUtc,
                l.JournalEntry.SourceDocumentType,
                l.JournalEntry.SourceDocumentNumber,
                Narration = l.Narration ?? l.JournalEntry.Narration,
                PartyName = l.Customer != null ? l.Customer.Name : (l.Supplier != null ? l.Supplier.Name : null),
                Debit = l.DebitAmount,
                Credit = l.CreditAmount,
                IsReversal = l.JournalEntry.PostingRole == JournalPostingRole.Reversal
            })
            .ToListAsync(cancellationToken);

        var reportLines = new List<LedgerReportLineDto>();
        decimal currentRunning = pageOpeningBalance;

        foreach (var item in pageLines)
        {
            currentRunning += (item.Debit - item.Credit);
            reportLines.Add(new LedgerReportLineDto
            {
                LineId = item.LineId,
                JournalEntryId = item.JournalEntryId,
                EntryNumber = item.EntryNumber,
                BusinessDate = item.BusinessDate,
                PostedAtUtc = item.PostedAtUtc,
                SourceDocumentType = item.SourceDocumentType,
                SourceDocumentNumber = item.SourceDocumentNumber,
                Narration = item.Narration,
                PartyName = item.PartyName,
                Debit = item.Debit,
                Credit = item.Credit,
                RunningBalance = currentRunning,
                IsReversal = item.IsReversal
            });
        }

        decimal closingBalance = openingBalance + periodDebit - periodCredit;

        return new LedgerReportDto
        {
            AccountId = accountId,
            AccountCode = account?.AccountCode,
            AccountName = account?.Name,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            TotalDebit = periodDebit,
            TotalCredit = periodCredit,
            ClosingBalance = closingBalance,
            TotalRows = totalRows,
            Lines = reportLines
        };
    }

    public async Task<LedgerReportDto> GetCustomerLedgerAsync(
        int customerId,
        int arControlAccountId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        decimal openingBalance = await context.JournalEntryLines
            .Where(l => l.CustomerId == customerId &&
                        l.AccountId == arControlAccountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate < fromDate)
            .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);

        var periodQuery = context.JournalEntryLines
            .Where(l => l.CustomerId == customerId &&
                        l.AccountId == arControlAccountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate >= fromDate &&
                        l.JournalEntry.BusinessDate <= toDate);

        int totalRows = await periodQuery.CountAsync(cancellationToken);
        decimal periodDebit = await periodQuery.SumAsync(l => l.DebitAmount, cancellationToken);
        decimal periodCredit = await periodQuery.SumAsync(l => l.CreditAmount, cancellationToken);

        int skip = (pageNumber - 1) * pageSize;
        decimal priorPagesNet = 0m;
        if (skip > 0)
        {
            priorPagesNet = await periodQuery
                .OrderBy(l => l.JournalEntry.BusinessDate)
                .ThenBy(l => l.JournalEntry.PostedAtUtc)
                .ThenBy(l => l.JournalEntryId)
                .ThenBy(l => l.Id)
                .Take(skip)
                .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);
        }

        decimal pageOpeningBalance = openingBalance + priorPagesNet;

        var pageLines = await periodQuery
            .OrderBy(l => l.JournalEntry.BusinessDate)
            .ThenBy(l => l.JournalEntry.PostedAtUtc)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(l => new
            {
                LineId = l.Id,
                l.JournalEntryId,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.BusinessDate,
                l.JournalEntry.PostedAtUtc,
                l.JournalEntry.SourceDocumentType,
                l.JournalEntry.SourceDocumentNumber,
                Narration = l.Narration ?? l.JournalEntry.Narration,
                Debit = l.DebitAmount,
                Credit = l.CreditAmount,
                IsReversal = l.JournalEntry.PostingRole == JournalPostingRole.Reversal
            })
            .ToListAsync(cancellationToken);

        var reportLines = new List<LedgerReportLineDto>();
        decimal currentRunning = pageOpeningBalance;

        foreach (var item in pageLines)
        {
            currentRunning += (item.Debit - item.Credit);
            reportLines.Add(new LedgerReportLineDto
            {
                LineId = item.LineId,
                JournalEntryId = item.JournalEntryId,
                EntryNumber = item.EntryNumber,
                BusinessDate = item.BusinessDate,
                PostedAtUtc = item.PostedAtUtc,
                SourceDocumentType = item.SourceDocumentType,
                SourceDocumentNumber = item.SourceDocumentNumber,
                Narration = item.Narration,
                PartyName = customer?.Name,
                Debit = item.Debit,
                Credit = item.Credit,
                RunningBalance = currentRunning,
                IsReversal = item.IsReversal
            });
        }

        decimal closingBalance = openingBalance + periodDebit - periodCredit;

        return new LedgerReportDto
        {
            PartyId = customerId,
            PartyName = customer?.Name,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            TotalDebit = periodDebit,
            TotalCredit = periodCredit,
            ClosingBalance = closingBalance,
            TotalRows = totalRows,
            Lines = reportLines
        };
    }

    public async Task<LedgerReportDto> GetSupplierLedgerAsync(
        int supplierId,
        int apControlAccountId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var supplier = await context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

        // Supplier ledger balance is Credit - Debit (Liability)
        decimal openingBalance = await context.JournalEntryLines
            .Where(l => l.SupplierId == supplierId &&
                        l.AccountId == apControlAccountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate < fromDate)
            .SumAsync(l => l.CreditAmount - l.DebitAmount, cancellationToken);

        var periodQuery = context.JournalEntryLines
            .Where(l => l.SupplierId == supplierId &&
                        l.AccountId == apControlAccountId &&
                        l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate >= fromDate &&
                        l.JournalEntry.BusinessDate <= toDate);

        int totalRows = await periodQuery.CountAsync(cancellationToken);
        decimal periodDebit = await periodQuery.SumAsync(l => l.DebitAmount, cancellationToken);
        decimal periodCredit = await periodQuery.SumAsync(l => l.CreditAmount, cancellationToken);

        int skip = (pageNumber - 1) * pageSize;
        decimal priorPagesNet = 0m;
        if (skip > 0)
        {
            priorPagesNet = await periodQuery
                .OrderBy(l => l.JournalEntry.BusinessDate)
                .ThenBy(l => l.JournalEntry.PostedAtUtc)
                .ThenBy(l => l.JournalEntryId)
                .ThenBy(l => l.Id)
                .Take(skip)
                .SumAsync(l => l.CreditAmount - l.DebitAmount, cancellationToken);
        }

        decimal pageOpeningBalance = openingBalance + priorPagesNet;

        var pageLines = await periodQuery
            .OrderBy(l => l.JournalEntry.BusinessDate)
            .ThenBy(l => l.JournalEntry.PostedAtUtc)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(l => new
            {
                LineId = l.Id,
                l.JournalEntryId,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.BusinessDate,
                l.JournalEntry.PostedAtUtc,
                l.JournalEntry.SourceDocumentType,
                l.JournalEntry.SourceDocumentNumber,
                Narration = l.Narration ?? l.JournalEntry.Narration,
                Debit = l.DebitAmount,
                Credit = l.CreditAmount,
                IsReversal = l.JournalEntry.PostingRole == JournalPostingRole.Reversal
            })
            .ToListAsync(cancellationToken);

        var reportLines = new List<LedgerReportLineDto>();
        decimal currentRunning = pageOpeningBalance;

        foreach (var item in pageLines)
        {
            currentRunning += (item.Credit - item.Debit);
            reportLines.Add(new LedgerReportLineDto
            {
                LineId = item.LineId,
                JournalEntryId = item.JournalEntryId,
                EntryNumber = item.EntryNumber,
                BusinessDate = item.BusinessDate,
                PostedAtUtc = item.PostedAtUtc,
                SourceDocumentType = item.SourceDocumentType,
                SourceDocumentNumber = item.SourceDocumentNumber,
                Narration = item.Narration,
                PartyName = supplier?.Name,
                Debit = item.Debit,
                Credit = item.Credit,
                RunningBalance = currentRunning,
                IsReversal = item.IsReversal
            });
        }

        decimal closingBalance = openingBalance + periodCredit - periodDebit;

        return new LedgerReportDto
        {
            PartyId = supplierId,
            PartyName = supplier?.Name,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            TotalDebit = periodDebit,
            TotalCredit = periodCredit,
            ClosingBalance = closingBalance,
            TotalRows = totalRows,
            Lines = reportLines
        };
    }

    public async Task<decimal> GetPartyRunningBalanceAsync(
        int partyId,
        bool isCustomer,
        int controlAccountId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (isCustomer)
        {
            return await context.JournalEntryLines
                .Where(l => l.CustomerId == partyId &&
                            l.AccountId == controlAccountId &&
                            l.JournalEntry.Status == JournalEntryStatus.Posted)
                .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);
        }
        else
        {
            return await context.JournalEntryLines
                .Where(l => l.SupplierId == partyId &&
                            l.AccountId == controlAccountId &&
                            l.JournalEntry.Status == JournalEntryStatus.Posted)
                .SumAsync(l => l.CreditAmount - l.DebitAmount, cancellationToken);
        }
    }

    public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var accounts = await context.Accounts
            .AsNoTracking()
            .OrderBy(a => a.AccountCode)
            .ToListAsync(cancellationToken);

        var lines = await context.JournalEntryLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate <= toDate)
            .Select(l => new
            {
                l.AccountId,
                l.JournalEntry.BusinessDate,
                l.DebitAmount,
                l.CreditAmount
            })
            .ToListAsync(cancellationToken);

        var linesByAccount = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.ToList());

        var tbLines = new List<TrialBalanceLineDto>();
        decimal totOpenDr = 0m, totOpenCr = 0m, totPerDr = 0m, totPerCr = 0m, totCloseDr = 0m, totCloseCr = 0m;

        foreach (var acc in accounts)
        {
            linesByAccount.TryGetValue(acc.Id, out var accLines);
            accLines ??= new();

            decimal openNet = accLines.Where(l => l.BusinessDate < fromDate).Sum(l => l.DebitAmount - l.CreditAmount);
            decimal perDr = accLines.Where(l => l.BusinessDate >= fromDate && l.BusinessDate <= toDate).Sum(l => l.DebitAmount);
            decimal perCr = accLines.Where(l => l.BusinessDate >= fromDate && l.BusinessDate <= toDate).Sum(l => l.CreditAmount);
            decimal closeNet = accLines.Sum(l => l.DebitAmount - l.CreditAmount);

            // Inactive accounts without any balance or activity are omitted
            if (!acc.IsActive && openNet == 0m && perDr == 0m && perCr == 0m && closeNet == 0m)
            {
                continue;
            }

            decimal openDr = openNet > 0 ? openNet : 0m;
            decimal openCr = openNet < 0 ? -openNet : 0m;

            decimal closeDr = closeNet > 0 ? closeNet : 0m;
            decimal closeCr = closeNet < 0 ? -closeNet : 0m;

            totOpenDr += openDr;
            totOpenCr += openCr;
            totPerDr += perDr;
            totPerCr += perCr;
            totCloseDr += closeDr;
            totCloseCr += closeCr;

            tbLines.Add(new TrialBalanceLineDto
            {
                AccountId = acc.Id,
                AccountCode = acc.AccountCode,
                AccountName = acc.Name,
                AccountType = acc.AccountType,
                IsActive = acc.IsActive,
                OpeningDebit = openDr,
                OpeningCredit = openCr,
                PeriodDebit = perDr,
                PeriodCredit = perCr,
                ClosingDebit = closeDr,
                ClosingCredit = closeCr
            });
        }

        return new TrialBalanceReportDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            TotalOpeningDebit = totOpenDr,
            TotalOpeningCredit = totOpenCr,
            TotalPeriodDebit = totPerDr,
            TotalPeriodCredit = totPerCr,
            TotalClosingDebit = totCloseDr,
            TotalClosingCredit = totCloseCr,
            Lines = tbLines
        };
    }

    public async Task<DayBookReportDto> GetDayBookAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entries = await context.JournalEntries
            .Where(j => j.BusinessDate == businessDate && j.Status == JournalEntryStatus.Posted)
            .OrderBy(j => j.PostedAtUtc)
            .ThenBy(j => j.Id)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Customer)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Supplier)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dayLines = entries.Select(j => new DayBookLineDto
        {
            JournalEntryId = j.Id,
            EntryNumber = j.EntryNumber,
            BusinessDate = j.BusinessDate,
            PostedAtUtc = j.PostedAtUtc,
            SourceDocumentType = j.SourceDocumentType,
            SourceDocumentNumber = j.SourceDocumentNumber,
            Narration = j.Narration,
            TotalDebit = j.TotalDebit,
            TotalCredit = j.TotalCredit,
            IsReversal = j.PostingRole == JournalPostingRole.Reversal,
            ReversesJournalEntryId = j.ReversesJournalEntryId,
            Lines = j.Lines.Select(l => new JournalEntryLineDto
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
        }).ToList();

        return new DayBookReportDto
        {
            BusinessDate = businessDate,
            TotalDebit = dayLines.Sum(e => e.TotalDebit),
            TotalCredit = dayLines.Sum(e => e.TotalCredit),
            Entries = dayLines
        };
    }

    public async Task<decimal> GetAccountBalanceAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var acc = await context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (acc == null) return 0m;

        decimal net = await context.JournalEntryLines
            .Where(l => l.AccountId == accountId && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);

        // Assets and Expenses normal balance is Debit (+Debit -Credit)
        // Liabilities, Equity, Revenue normal balance is Credit (+Credit -Debit)
        if (acc.AccountType == AccountType.Asset || acc.AccountType == AccountType.Expense)
        {
            return net;
        }
        else
        {
            return -net;
        }
    }

    public async Task<decimal> GetTotalSubledgerBalanceAsync(int controlAccountId, bool isCustomer, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (isCustomer)
        {
            return await context.JournalEntryLines
                .Where(l => l.AccountId == controlAccountId && l.CustomerId != null && l.JournalEntry.Status == JournalEntryStatus.Posted)
                .SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);
        }
        else
        {
            return await context.JournalEntryLines
                .Where(l => l.AccountId == controlAccountId && l.SupplierId != null && l.JournalEntry.Status == JournalEntryStatus.Posted)
                .SumAsync(l => l.CreditAmount - l.DebitAmount, cancellationToken);
        }
    }

    public async Task<ProfitAndLossStatementDto> GetProfitAndLossAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(a => a.AccountType == AccountType.Revenue || a.AccountType == AccountType.Expense)
            .OrderBy(a => a.AccountCode)
            .ToListAsync(cancellationToken);

        var lines = await context.JournalEntryLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate >= fromDate &&
                        l.JournalEntry.BusinessDate <= toDate &&
                        (l.Account.AccountType == AccountType.Revenue || l.Account.AccountType == AccountType.Expense))
            .Select(l => new
            {
                l.AccountId,
                l.DebitAmount,
                l.CreditAmount
            })
            .ToListAsync(cancellationToken);

        var linesByAccount = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.ToList());

        var revenueLines = new List<FinancialStatementLineDto>();
        var expenseLines = new List<FinancialStatementLineDto>();

        decimal totalRevenue = 0m;
        decimal totalExpense = 0m;

        foreach (var acc in accounts)
        {
            linesByAccount.TryGetValue(acc.Id, out var accLines);
            accLines ??= new();

            if (acc.AccountType == AccountType.Revenue)
            {
                // Revenue: Normal balance Credit (Credit - Debit)
                decimal amount = accLines.Sum(l => l.CreditAmount - l.DebitAmount);
                if (amount != 0m || (acc.AllowPosting && acc.IsActive))
                {
                    revenueLines.Add(new FinancialStatementLineDto
                    {
                        AccountId = acc.Id,
                        AccountCode = acc.AccountCode,
                        AccountName = acc.Name,
                        Amount = amount
                    });
                    totalRevenue += amount;
                }
            }
            else if (acc.AccountType == AccountType.Expense)
            {
                // Expense: Normal balance Debit (Debit - Credit)
                decimal amount = accLines.Sum(l => l.DebitAmount - l.CreditAmount);
                if (amount != 0m || (acc.AllowPosting && acc.IsActive))
                {
                    expenseLines.Add(new FinancialStatementLineDto
                    {
                        AccountId = acc.Id,
                        AccountCode = acc.AccountCode,
                        AccountName = acc.Name,
                        Amount = amount
                    });
                    totalExpense += amount;
                }
            }
        }

        decimal netProfitOrLoss = totalRevenue - totalExpense;

        return new ProfitAndLossStatementDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            RevenueLines = revenueLines,
            TotalRevenue = totalRevenue,
            ExpenseLines = expenseLines,
            TotalExpense = totalExpense,
            NetProfitOrLoss = netProfitOrLoss,
            IsProfit = netProfitOrLoss >= 0m
        };
    }

    public async Task<BalanceSheetStatementDto> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var accounts = await context.Accounts
            .AsNoTracking()
            .OrderBy(a => a.AccountCode)
            .ToListAsync(cancellationToken);

        var lines = await context.JournalEntryLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted &&
                        l.JournalEntry.BusinessDate <= asOfDate)
            .Select(l => new
            {
                l.AccountId,
                l.DebitAmount,
                l.CreditAmount
            })
            .ToListAsync(cancellationToken);

        var linesByAccount = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.ToList());

        var assetLines = new List<FinancialStatementLineDto>();
        var liabilityLines = new List<FinancialStatementLineDto>();
        var equityLines = new List<FinancialStatementLineDto>();

        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal totalEquity = 0m;
        decimal periodEarnings = 0m;

        foreach (var acc in accounts)
        {
            linesByAccount.TryGetValue(acc.Id, out var accLines);
            accLines ??= new();

            decimal dr = accLines.Sum(l => l.DebitAmount);
            decimal cr = accLines.Sum(l => l.CreditAmount);

            switch (acc.AccountType)
            {
                case AccountType.Asset:
                    decimal assetVal = dr - cr;
                    if (assetVal != 0m || (acc.AllowPosting && acc.IsActive))
                    {
                        assetLines.Add(new FinancialStatementLineDto
                        {
                            AccountId = acc.Id,
                            AccountCode = acc.AccountCode,
                            AccountName = acc.Name,
                            Amount = assetVal
                        });
                        totalAssets += assetVal;
                    }
                    break;

                case AccountType.Liability:
                    decimal liabVal = cr - dr;
                    if (liabVal != 0m || (acc.AllowPosting && acc.IsActive))
                    {
                        liabilityLines.Add(new FinancialStatementLineDto
                        {
                            AccountId = acc.Id,
                            AccountCode = acc.AccountCode,
                            AccountName = acc.Name,
                            Amount = liabVal
                        });
                        totalLiabilities += liabVal;
                    }
                    break;

                case AccountType.Equity:
                    decimal eqVal = cr - dr;
                    if (eqVal != 0m || (acc.AllowPosting && acc.IsActive))
                    {
                        equityLines.Add(new FinancialStatementLineDto
                        {
                            AccountId = acc.Id,
                            AccountCode = acc.AccountCode,
                            AccountName = acc.Name,
                            Amount = eqVal
                        });
                        totalEquity += eqVal;
                    }
                    break;

                case AccountType.Revenue:
                    periodEarnings += (cr - dr);
                    break;

                case AccountType.Expense:
                    periodEarnings -= (dr - cr);
                    break;
            }
        }

        decimal totalLiabAndEquity = totalLiabilities + totalEquity + periodEarnings;
        decimal diff = totalAssets - totalLiabAndEquity;

        return new BalanceSheetStatementDto
        {
            AsOfDate = asOfDate,
            AssetLines = assetLines,
            TotalAssets = totalAssets,
            LiabilityLines = liabilityLines,
            TotalLiabilities = totalLiabilities,
            EquityLines = equityLines,
            TotalEquity = totalEquity,
            CurrentPeriodEarnings = periodEarnings,
            TotalLiabilitiesAndEquity = totalLiabAndEquity,
            Difference = diff,
            IsBalanced = diff == 0m
        };
    }
}
