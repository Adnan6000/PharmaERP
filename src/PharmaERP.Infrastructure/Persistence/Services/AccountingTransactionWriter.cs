using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class AccountingTransactionWriter : IAccountingTransactionWriter
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IBusinessClock _businessClock;
    private readonly IAppConfigRepository _appConfigRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly ILogger<AccountingTransactionWriter> _logger;

    public AccountingTransactionWriter(
        IDbContextFactory<AppDbContext> contextFactory,
        IBusinessClock businessClock,
        IAppConfigRepository appConfigRepository,
        IAccountRepository accountRepository,
        IDocumentNumberGenerator documentNumberGenerator,
        ILogger<AccountingTransactionWriter> logger)
    {
        _contextFactory = contextFactory;
        _businessClock = businessClock;
        _appConfigRepository = appConfigRepository;
        _accountRepository = accountRepository;
        _documentNumberGenerator = documentNumberGenerator;
        _logger = logger;
    }

    public async Task<ReceiptVoucherDto> CreateAndPostReceiptVoucherAsync(
        string voucherNumber,
        string journalEntryNumber,
        ReceiptVoucherCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
            var businessDate = dto.BusinessDate ?? currentBusinessDate;

            await CheckLockDateAsync(businessDate, cancellationToken);

            var voucher = new ReceiptVoucher
            {
                VoucherNumber = voucherNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                CashOrBankAccountId = dto.CashOrBankAccountId,
                OffsetAccountId = dto.OffsetAccountId,
                CustomerId = dto.CustomerId,
                Amount = dto.Amount,
                Reference = dto.Reference?.Trim(),
                Narration = dto.Narration?.Trim(),
                Status = VoucherStatus.Posted,
                OperationId = dto.OperationId
            };

            await context.ReceiptVouchers.AddAsync(voucher, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // Two-phase journal posting
            var journal = new JournalEntry
            {
                EntryNumber = journalEntryNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Narration = dto.Narration?.Trim() ?? $"Receipt Voucher: {voucherNumber}",
                SourceDocumentType = JournalSourceDocumentType.ReceiptVoucher,
                SourceDocumentId = voucher.Id,
                SourceDocumentNumber = voucherNumber,
                PostingRole = JournalPostingRole.Primary,
                Status = JournalEntryStatus.Draft,
                OperationId = Guid.NewGuid()
            };

            var line1 = new JournalEntryLine
            {
                AccountId = dto.CashOrBankAccountId,
                DebitAmount = dto.Amount,
                CreditAmount = 0m,
                Narration = $"Receipt: {voucherNumber}"
            };

            var line2 = new JournalEntryLine
            {
                AccountId = dto.OffsetAccountId,
                DebitAmount = 0m,
                CreditAmount = dto.Amount,
                CustomerId = dto.CustomerId,
                Narration = $"Receipt: {voucherNumber}"
            };

            journal.Lines.Add(line1);
            journal.Lines.Add(line2);

            await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ReceiptVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                CashOrBankAccountId = voucher.CashOrBankAccountId,
                OffsetAccountId = voucher.OffsetAccountId,
                CustomerId = voucher.CustomerId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReceiptVoucherDto> CancelReceiptVoucherAsync(
        int voucherId,
        string reversalEntryNumber,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var voucher = await context.ReceiptVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
                ?? throw new NotFoundException($"Receipt voucher with ID {voucherId} was not found.");

            if (voucher.Status == VoucherStatus.Cancelled)
                throw new ValidationException($"Receipt voucher '{voucher.VoucherNumber}' is already cancelled.");

            bool hasAllocations = await context.ReceiptVoucherAllocations
                .AnyAsync(a => a.ReceiptVoucherId == voucher.Id && a.Status == AllocationStatus.Active, cancellationToken);
            if (hasAllocations)
                throw new ValidationException($"Cannot cancel receipt voucher '{voucher.VoucherNumber}' because it has active invoice settlement allocations. Remove or void allocations first.");


            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);

            await CheckLockDateAsync(currentBusinessDate, cancellationToken);

            voucher.Status = VoucherStatus.Cancelled;
            voucher.CancelledAtUtc = serverUtc;
            voucher.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            var origJournal = await context.JournalEntries
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.ReceiptVoucher &&
                                          j.SourceDocumentId == voucher.Id &&
                                          j.PostingRole == JournalPostingRole.Primary, cancellationToken)
                ?? throw new InvalidOperationException($"Original journal entry for receipt voucher '{voucher.VoucherNumber}' not found.");

            var revJournal = CreateReversalJournal(origJournal, reversalEntryNumber, currentBusinessDate, serverUtc, cancellationReason);
            await PostTwoPhaseJournalAsync(context, revJournal, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ReceiptVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                CashOrBankAccountId = voucher.CashOrBankAccountId,
                OffsetAccountId = voucher.OffsetAccountId,
                CustomerId = voucher.CustomerId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId,
                CancelledAtUtc = voucher.CancelledAtUtc,
                CancellationReason = voucher.CancellationReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentVoucherDto> CreateAndPostPaymentVoucherAsync(
        string voucherNumber,
        string journalEntryNumber,
        PaymentVoucherCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
            var businessDate = dto.BusinessDate ?? currentBusinessDate;

            await CheckLockDateAsync(businessDate, cancellationToken);

            var voucher = new PaymentVoucher
            {
                VoucherNumber = voucherNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                CashOrBankAccountId = dto.CashOrBankAccountId,
                OffsetAccountId = dto.OffsetAccountId,
                SupplierId = dto.SupplierId,
                Amount = dto.Amount,
                Reference = dto.Reference?.Trim(),
                Narration = dto.Narration?.Trim(),
                Status = VoucherStatus.Posted,
                OperationId = dto.OperationId
            };

            await context.PaymentVouchers.AddAsync(voucher, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var journal = new JournalEntry
            {
                EntryNumber = journalEntryNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Narration = dto.Narration?.Trim() ?? $"Payment Voucher: {voucherNumber}",
                SourceDocumentType = JournalSourceDocumentType.PaymentVoucher,
                SourceDocumentId = voucher.Id,
                SourceDocumentNumber = voucherNumber,
                PostingRole = JournalPostingRole.Primary,
                Status = JournalEntryStatus.Draft,
                OperationId = Guid.NewGuid()
            };

            var line1 = new JournalEntryLine
            {
                AccountId = dto.OffsetAccountId,
                DebitAmount = dto.Amount,
                CreditAmount = 0m,
                SupplierId = dto.SupplierId,
                Narration = $"Payment: {voucherNumber}"
            };

            var line2 = new JournalEntryLine
            {
                AccountId = dto.CashOrBankAccountId,
                DebitAmount = 0m,
                CreditAmount = dto.Amount,
                Narration = $"Payment: {voucherNumber}"
            };

            journal.Lines.Add(line1);
            journal.Lines.Add(line2);

            await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PaymentVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                CashOrBankAccountId = voucher.CashOrBankAccountId,
                OffsetAccountId = voucher.OffsetAccountId,
                SupplierId = voucher.SupplierId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentVoucherDto> CancelPaymentVoucherAsync(
        int voucherId,
        string reversalEntryNumber,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var voucher = await context.PaymentVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
                ?? throw new NotFoundException($"Payment voucher with ID {voucherId} was not found.");

            if (voucher.Status == VoucherStatus.Cancelled)
                throw new ValidationException($"Payment voucher '{voucher.VoucherNumber}' is already cancelled.");

            bool hasAllocations = await context.PaymentVoucherAllocations
                .AnyAsync(a => a.PaymentVoucherId == voucher.Id && a.Status == AllocationStatus.Active, cancellationToken);
            if (hasAllocations)
                throw new ValidationException($"Cannot cancel payment voucher '{voucher.VoucherNumber}' because it has active invoice settlement allocations. Remove or void allocations first.");


            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);

            await CheckLockDateAsync(currentBusinessDate, cancellationToken);

            voucher.Status = VoucherStatus.Cancelled;
            voucher.CancelledAtUtc = serverUtc;
            voucher.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            var origJournal = await context.JournalEntries
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PaymentVoucher &&
                                          j.SourceDocumentId == voucher.Id &&
                                          j.PostingRole == JournalPostingRole.Primary, cancellationToken)
                ?? throw new InvalidOperationException($"Original journal entry for payment voucher '{voucher.VoucherNumber}' not found.");

            var revJournal = CreateReversalJournal(origJournal, reversalEntryNumber, currentBusinessDate, serverUtc, cancellationReason);
            await PostTwoPhaseJournalAsync(context, revJournal, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new PaymentVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                CashOrBankAccountId = voucher.CashOrBankAccountId,
                OffsetAccountId = voucher.OffsetAccountId,
                SupplierId = voucher.SupplierId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId,
                CancelledAtUtc = voucher.CancelledAtUtc,
                CancellationReason = voucher.CancellationReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<JournalVoucherDto> CreateAndPostJournalVoucherAsync(
        string voucherNumber,
        string journalEntryNumber,
        JournalVoucherCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
            var businessDate = dto.BusinessDate ?? currentBusinessDate;

            await CheckLockDateAsync(businessDate, cancellationToken);

            decimal totalAmount = dto.Lines.Sum(l => l.DebitAmount);

            var voucher = new JournalVoucher
            {
                VoucherNumber = voucherNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Reference = dto.Reference?.Trim(),
                Narration = dto.Narration?.Trim(),
                TotalAmount = totalAmount,
                Status = VoucherStatus.Posted,
                OperationId = dto.OperationId
            };

            await context.JournalVouchers.AddAsync(voucher, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var journal = new JournalEntry
            {
                EntryNumber = journalEntryNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Narration = dto.Narration?.Trim() ?? $"Journal Voucher: {voucherNumber}",
                SourceDocumentType = JournalSourceDocumentType.JournalVoucher,
                SourceDocumentId = voucher.Id,
                SourceDocumentNumber = voucherNumber,
                PostingRole = JournalPostingRole.Primary,
                Status = JournalEntryStatus.Draft,
                OperationId = Guid.NewGuid()
            };

            foreach (var l in dto.Lines)
            {
                journal.Lines.Add(new JournalEntryLine
                {
                    AccountId = l.AccountId,
                    DebitAmount = l.DebitAmount,
                    CreditAmount = l.CreditAmount,
                    CustomerId = l.CustomerId,
                    SupplierId = l.SupplierId,
                    Narration = l.Narration?.Trim()
                });
            }

            await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new JournalVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                TotalAmount = voucher.TotalAmount,
                Status = voucher.Status,
                OperationId = voucher.OperationId
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<JournalVoucherDto> CancelJournalVoucherAsync(
        int voucherId,
        string reversalEntryNumber,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var voucher = await context.JournalVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
                ?? throw new NotFoundException($"Journal voucher with ID {voucherId} was not found.");

            if (voucher.Status == VoucherStatus.Cancelled)
                throw new ValidationException($"Journal voucher '{voucher.VoucherNumber}' is already cancelled.");

            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);

            await CheckLockDateAsync(currentBusinessDate, cancellationToken);

            voucher.Status = VoucherStatus.Cancelled;
            voucher.CancelledAtUtc = serverUtc;
            voucher.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            var origJournal = await context.JournalEntries
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.JournalVoucher &&
                                          j.SourceDocumentId == voucher.Id &&
                                          j.PostingRole == JournalPostingRole.Primary, cancellationToken)
                ?? throw new InvalidOperationException($"Original journal entry for journal voucher '{voucher.VoucherNumber}' not found.");

            var revJournal = CreateReversalJournal(origJournal, reversalEntryNumber, currentBusinessDate, serverUtc, cancellationReason);
            await PostTwoPhaseJournalAsync(context, revJournal, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new JournalVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                TotalAmount = voucher.TotalAmount,
                Status = voucher.Status,
                OperationId = voucher.OperationId,
                CancelledAtUtc = voucher.CancelledAtUtc,
                CancellationReason = voucher.CancellationReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OpeningBalanceVoucherDto> CreateAndPostOpeningBalanceVoucherAsync(
        string voucherNumber,
        string journalEntryNumber,
        OpeningBalanceVoucherCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
            var businessDate = dto.BusinessDate ?? currentBusinessDate;

            await CheckLockDateAsync(businessDate, cancellationToken);

            var voucher = new OpeningBalanceVoucher
            {
                VoucherNumber = voucherNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Reference = dto.Reference?.Trim(),
                Narration = dto.Narration?.Trim(),
                Status = VoucherStatus.Posted,
                OperationId = dto.OperationId
            };

            await context.OpeningBalanceVouchers.AddAsync(voucher, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var journal = new JournalEntry
            {
                EntryNumber = journalEntryNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Narration = dto.Narration?.Trim() ?? $"Opening Balance: {voucherNumber}",
                SourceDocumentType = JournalSourceDocumentType.OpeningBalance,
                SourceDocumentId = voucher.Id,
                SourceDocumentNumber = voucherNumber,
                PostingRole = JournalPostingRole.Primary,
                Status = JournalEntryStatus.Draft,
                OperationId = Guid.NewGuid()
            };

            decimal sumDebits = 0m;
            decimal sumCredits = 0m;

            foreach (var l in dto.Lines)
            {
                journal.Lines.Add(new JournalEntryLine
                {
                    AccountId = l.AccountId,
                    DebitAmount = l.DebitAmount,
                    CreditAmount = l.CreditAmount,
                    CustomerId = l.CustomerId,
                    SupplierId = l.SupplierId,
                    Narration = l.Narration?.Trim()
                });

                sumDebits += l.DebitAmount;
                sumCredits += l.CreditAmount;
            }

            decimal netDifference = sumDebits - sumCredits;
            if (netDifference != 0m)
            {
                var equityAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity, cancellationToken)
                    ?? throw new ValidationException("Opening Balance Equity system account is not configured.");

                if (netDifference > 0)
                {
                    journal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = equityAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = netDifference,
                        Narration = "Balancing entry to Opening Balance Equity"
                    });
                }
                else
                {
                    journal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = equityAcc.Id,
                        DebitAmount = -netDifference,
                        CreditAmount = 0m,
                        Narration = "Balancing entry to Opening Balance Equity"
                    });
                }
            }

            await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new OpeningBalanceVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OpeningBalanceVoucherDto> CancelOpeningBalanceVoucherAsync(
        int voucherId,
        string reversalEntryNumber,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var voucher = await context.OpeningBalanceVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
                ?? throw new NotFoundException($"Opening balance voucher with ID {voucherId} was not found.");

            if (voucher.Status == VoucherStatus.Cancelled)
                throw new ValidationException($"Opening balance voucher '{voucher.VoucherNumber}' is already cancelled.");

            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);

            await CheckLockDateAsync(currentBusinessDate, cancellationToken);

            voucher.Status = VoucherStatus.Cancelled;
            voucher.CancelledAtUtc = serverUtc;
            voucher.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            var origJournal = await context.JournalEntries
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.OpeningBalance &&
                                          j.SourceDocumentId == voucher.Id &&
                                          j.PostingRole == JournalPostingRole.Primary, cancellationToken)
                ?? throw new InvalidOperationException($"Original journal entry for opening balance voucher '{voucher.VoucherNumber}' not found.");

            var revJournal = CreateReversalJournal(origJournal, reversalEntryNumber, currentBusinessDate, serverUtc, cancellationReason);
            await PostTwoPhaseJournalAsync(context, revJournal, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new OpeningBalanceVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId,
                CancelledAtUtc = voucher.CancelledAtUtc,
                CancellationReason = voucher.CancellationReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ContraVoucherDto> CreateAndPostContraVoucherAsync(
        string voucherNumber,
        string journalEntryNumber,
        ContraVoucherCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
            var businessDate = dto.BusinessDate ?? currentBusinessDate;

            await CheckLockDateAsync(businessDate, cancellationToken);

            var voucher = new ContraVoucher
            {
                VoucherNumber = voucherNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                SourceAccountId = dto.SourceAccountId,
                DestinationAccountId = dto.DestinationAccountId,
                Amount = dto.Amount,
                Reference = dto.Reference?.Trim(),
                Narration = dto.Narration?.Trim(),
                Status = VoucherStatus.Posted,
                OperationId = dto.OperationId
            };

            await context.ContraVouchers.AddAsync(voucher, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // Two-phase journal posting
            var journal = new JournalEntry
            {
                EntryNumber = journalEntryNumber,
                BusinessDate = businessDate,
                PostedAtUtc = serverUtc,
                Narration = dto.Narration?.Trim() ?? $"Contra Voucher: {voucherNumber}",
                SourceDocumentType = JournalSourceDocumentType.ContraVoucher,
                SourceDocumentId = voucher.Id,
                SourceDocumentNumber = voucherNumber,
                PostingRole = JournalPostingRole.Primary,
                Status = JournalEntryStatus.Draft,
                OperationId = Guid.NewGuid()
            };

            // Destination account receives funds -> Debit
            var lineDebit = new JournalEntryLine
            {
                AccountId = dto.DestinationAccountId,
                DebitAmount = dto.Amount,
                CreditAmount = 0m,
                Narration = $"Contra: {voucherNumber} (Funds Received)"
            };

            // Source account disburses funds -> Credit
            var lineCredit = new JournalEntryLine
            {
                AccountId = dto.SourceAccountId,
                DebitAmount = 0m,
                CreditAmount = dto.Amount,
                Narration = $"Contra: {voucherNumber} (Funds Disbursed)"
            };

            journal.Lines.Add(lineDebit);
            journal.Lines.Add(lineCredit);

            await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ContraVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                SourceAccountId = voucher.SourceAccountId,
                DestinationAccountId = voucher.DestinationAccountId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ContraVoucherDto> CancelContraVoucherAsync(
        int voucherId,
        string reversalEntryNumber,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var voucher = await context.ContraVouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
                ?? throw new NotFoundException($"Contra voucher with ID {voucherId} was not found.");

            if (voucher.Status == VoucherStatus.Cancelled)
                throw new ValidationException($"Contra voucher '{voucher.VoucherNumber}' is already cancelled.");

            var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
            var currentBusinessDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);

            await CheckLockDateAsync(currentBusinessDate, cancellationToken);

            voucher.Status = VoucherStatus.Cancelled;
            voucher.CancelledAtUtc = serverUtc;
            voucher.CancellationReason = cancellationReason;

            await context.SaveChangesAsync(cancellationToken);

            var origJournal = await context.JournalEntries
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.ContraVoucher &&
                                          j.SourceDocumentId == voucher.Id &&
                                          j.PostingRole == JournalPostingRole.Primary, cancellationToken)
                ?? throw new InvalidOperationException($"Original journal entry for contra voucher '{voucher.VoucherNumber}' not found.");

            var revJournal = CreateReversalJournal(origJournal, reversalEntryNumber, currentBusinessDate, serverUtc, cancellationReason);
            await PostTwoPhaseJournalAsync(context, revJournal, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ContraVoucherDto
            {
                Id = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                BusinessDate = voucher.BusinessDate,
                PostedAtUtc = voucher.PostedAtUtc,
                SourceAccountId = voucher.SourceAccountId,
                DestinationAccountId = voucher.DestinationAccountId,
                Amount = voucher.Amount,
                Reference = voucher.Reference,
                Narration = voucher.Narration,
                Status = voucher.Status,
                OperationId = voucher.OperationId,
                CancelledAtUtc = voucher.CancelledAtUtc,
                CancellationReason = voucher.CancellationReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<JournalEntryDto> PostManualJournalAsync(JournalEntry entry, List<JournalEntryLine> lines, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            entry.Lines = lines;
            await PostTwoPhaseJournalAsync(context, entry, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new JournalEntryDto
            {
                Id = entry.Id,
                EntryNumber = entry.EntryNumber,
                BusinessDate = entry.BusinessDate,
                PostedAtUtc = entry.PostedAtUtc,
                TotalDebit = entry.TotalDebit,
                TotalCredit = entry.TotalCredit,
                Status = entry.Status
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task PostTwoPhaseJournalAsync(AppDbContext context, JournalEntry journal, CancellationToken cancellationToken)
    {
        // Step 1: Insert header as Draft with lines
        journal.Status = JournalEntryStatus.Draft;
        journal.TotalDebit = 0m;
        journal.TotalCredit = 0m;

        await context.JournalEntries.AddAsync(journal, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Step 2: Set verified totals and transition to Posted
        decimal sumDr = journal.Lines.Sum(l => l.DebitAmount);
        decimal sumCr = journal.Lines.Sum(l => l.CreditAmount);

        journal.TotalDebit = sumDr;
        journal.TotalCredit = sumCr;
        journal.Status = JournalEntryStatus.Posted;

        // Triggers validate balance and constraints on UPDATE to Posted
        await context.SaveChangesAsync(cancellationToken);
    }

    public static JournalEntry CreateReversalJournal(
        JournalEntry orig,
        string reversalEntryNumber,
        DateOnly reversalBusinessDate,
        DateTime reversalUtc,
        string reason)
    {
        var rev = new JournalEntry
        {
            EntryNumber = reversalEntryNumber,
            BusinessDate = reversalBusinessDate,
            PostedAtUtc = reversalUtc,
            Narration = $"Reversal of {orig.EntryNumber}: {reason}",
            SourceDocumentType = orig.SourceDocumentType,
            SourceDocumentId = orig.SourceDocumentId,
            SourceDocumentNumber = orig.SourceDocumentNumber,
            PostingRole = JournalPostingRole.Reversal,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid(),
            ReversesJournalEntryId = orig.Id
        };

        foreach (var l in orig.Lines)
        {
            rev.Lines.Add(new JournalEntryLine
            {
                AccountId = l.AccountId,
                DebitAmount = l.CreditAmount, // Swapped
                CreditAmount = l.DebitAmount, // Swapped
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId,
                Narration = $"Reversal line of {orig.EntryNumber}"
            });
        }

        return rev;
    }

    private async Task CheckLockDateAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        string? raw = await _appConfigRepository.GetValueAsync("Accounting.LockDate", cancellationToken);
        if (!string.IsNullOrWhiteSpace(raw) && DateOnly.TryParse(raw, out var lockDate))
        {
            if (businessDate <= lockDate)
            {
                throw new ValidationException($"Cannot post transaction: The accounting period on or before {lockDate:yyyy-MM-dd} is closed.");
            }
        }
    }

    private async Task<AccountingSetupState> GetSetupStateAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var config = await context.AppConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Key == "Accounting.SetupState", cancellationToken);
        if (config == null || string.IsNullOrWhiteSpace(config.Value))
            return AccountingSetupState.NotConfigured;

        if (Enum.TryParse<AccountingSetupState>(config.Value, out var state))
            return state;

        if (int.TryParse(config.Value, out var sInt) && Enum.IsDefined(typeof(AccountingSetupState), sInt))
            return (AccountingSetupState)sInt;

        return AccountingSetupState.NotConfigured;
    }

    private static async Task<Account> GetRequiredAccountAsync(AppDbContext context, SystemAccountType type, CancellationToken cancellationToken)
    {
        var acc = await context.Accounts.FirstOrDefaultAsync(a => a.SystemAccountType == type && a.IsActive, cancellationToken);
        if (acc == null)
            throw new ValidationException($"Required system account '{type}' is not configured or is inactive.");
        return acc;
    }

    public async Task PostSaleInvoiceJournalAsync(
        object dbContext,
        SaleInvoice invoice,
        decimal totalCogsValue,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");
        if (state == AccountingSetupState.Initializing)
            throw new ValidationException("Accounting is currently initializing; retry transaction shortly.");

        if (invoice.TaxAmount != 0m)
            throw new ValidationException($"Tax amount must be zero for accounting posting (Tax: {invoice.TaxAmount}).");

        await CheckLockDateAsync(invoice.BusinessDate, cancellationToken);

        var revAcc = await GetRequiredAccountAsync(context, SystemAccountType.SalesRevenue, cancellationToken);
        var sDiscAcc = await GetRequiredAccountAsync(context, SystemAccountType.SalesDiscount, cancellationToken);
        var invAcc = await GetRequiredAccountAsync(context, SystemAccountType.Inventory, cancellationToken);
        var cogsAcc = await GetRequiredAccountAsync(context, SystemAccountType.CostOfGoodsSold, cancellationToken);

        Account receivableOrCashAcc;
        if (invoice.SaleType == SaleType.Cash)
        {
            receivableOrCashAcc = await GetRequiredAccountAsync(context, SystemAccountType.CashOnHand, cancellationToken);
        }
        else
        {
            receivableOrCashAcc = await GetRequiredAccountAsync(context, SystemAccountType.AccountsReceivableControl, cancellationToken);
        }

        string entryNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, invoice.BusinessDate.Year, cancellationToken);
        var journal = new JournalEntry
        {
            EntryNumber = entryNum,
            BusinessDate = invoice.BusinessDate,
            PostedAtUtc = invoice.PostedAtUtc ?? DateTime.UtcNow,
            Narration = $"Sale Invoice: {invoice.InvoiceNumber}",
            SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
            SourceDocumentId = invoice.Id,
            SourceDocumentNumber = invoice.InvoiceNumber,
            PostingRole = JournalPostingRole.Primary,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid()
        };

        decimal netAmount = invoice.NetTotal;
        decimal discountAmount = invoice.LineDiscountTotal + invoice.InvoiceDiscountAmount;
        decimal subTotal = invoice.GrossTotal;

        // Line 1: Cash or AR
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = receivableOrCashAcc.Id,
            DebitAmount = netAmount,
            CreditAmount = 0m,
            CustomerId = invoice.SaleType == SaleType.Credit ? invoice.CustomerId : null,
            Narration = $"Sale Invoice: {invoice.InvoiceNumber}"
        });

        // Line 2: Sales Discount (if > 0)
        if (discountAmount > 0m)
        {
            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = sDiscAcc.Id,
                DebitAmount = discountAmount,
                CreditAmount = 0m,
                Narration = $"Sales Discount: {invoice.InvoiceNumber}"
            });
        }

        // Line 3: Revenue
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = revAcc.Id,
            DebitAmount = 0m,
            CreditAmount = subTotal,
            Narration = $"Sales Revenue: {invoice.InvoiceNumber}"
        });

        // Lines 4 & 5: COGS & Inventory
        if (totalCogsValue > 0m)
        {
            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = cogsAcc.Id,
                DebitAmount = totalCogsValue,
                CreditAmount = 0m,
                Narration = $"COGS for {invoice.InvoiceNumber}"
            });

            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = invAcc.Id,
                DebitAmount = 0m,
                CreditAmount = totalCogsValue,
                Narration = $"Inventory Outflow: {invoice.InvoiceNumber}"
            });
        }

        await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
    }

    public async Task PostSaleInvoiceReversalAsync(
        object dbContext,
        SaleInvoice invoice,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");

        var orig = await context.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice &&
                                      j.SourceDocumentId == invoice.Id &&
                                      j.PostingRole == JournalPostingRole.Primary &&
                                      j.Status == JournalEntryStatus.Posted, cancellationToken);

        if (orig == null) return;

        var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        var revDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        await CheckLockDateAsync(revDate, cancellationToken);

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);
        var revJ = CreateReversalJournal(orig, revNum, revDate, serverUtc, reason);

        await PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
    }

    public async Task PostSaleReturnJournalAsync(
        object dbContext,
        SaleReturn saleReturn,
        decimal totalRestoredInventoryValue,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");
        if (state == AccountingSetupState.Initializing)
            throw new ValidationException("Accounting is currently initializing; retry transaction shortly.");

        await CheckLockDateAsync(saleReturn.ReturnDate, cancellationToken);

        var sRetAcc = await GetRequiredAccountAsync(context, SystemAccountType.SalesReturns, cancellationToken);
        var sDiscAcc = await GetRequiredAccountAsync(context, SystemAccountType.SalesDiscount, cancellationToken);
        var invAcc = await GetRequiredAccountAsync(context, SystemAccountType.Inventory, cancellationToken);
        var cogsAcc = await GetRequiredAccountAsync(context, SystemAccountType.CostOfGoodsSold, cancellationToken);

        var origSale = saleReturn.OriginalSaleInvoice ?? await context.SaleInvoices.FindAsync(new object[] { saleReturn.OriginalSaleInvoiceId }, cancellationToken);

        Account refundAcc;
        if (origSale?.SaleType == SaleType.Cash)
        {
            refundAcc = await GetRequiredAccountAsync(context, SystemAccountType.CashOnHand, cancellationToken);
        }
        else
        {
            refundAcc = await GetRequiredAccountAsync(context, SystemAccountType.AccountsReceivableControl, cancellationToken);
        }

        decimal restoredInv = totalRestoredInventoryValue;
        decimal grossReturned = saleReturn.Items.Sum(i => i.Quantity * (i.OriginalSaleInvoiceItem != null ? i.OriginalSaleInvoiceItem.UnitSalePrice : 0m));
        if (grossReturned == 0m) grossReturned = saleReturn.TotalAmount;

        decimal netRefund = saleReturn.TotalAmount;
        decimal discountReversed = grossReturned - netRefund;
        if (discountReversed < 0m) discountReversed = 0m;

        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, saleReturn.ReturnDate.Year, cancellationToken);

        var journal = new JournalEntry
        {
            EntryNumber = jNum,
            BusinessDate = saleReturn.ReturnDate,
            PostedAtUtc = saleReturn.PostedAtUtc ?? DateTime.UtcNow,
            Narration = $"Sale Return: {saleReturn.ReturnNumber}",
            SourceDocumentType = JournalSourceDocumentType.SaleReturn,
            SourceDocumentId = saleReturn.Id,
            SourceDocumentNumber = saleReturn.ReturnNumber,
            PostingRole = JournalPostingRole.Primary,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid()
        };

        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = sRetAcc.Id,
            DebitAmount = grossReturned,
            CreditAmount = 0m,
            Narration = $"Sale Return: {saleReturn.ReturnNumber}"
        });

        if (discountReversed > 0m)
        {
            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = sDiscAcc.Id,
                DebitAmount = 0m,
                CreditAmount = discountReversed,
                Narration = $"Discount Reversed: {saleReturn.ReturnNumber}"
            });
        }

        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = refundAcc.Id,
            DebitAmount = 0m,
            CreditAmount = netRefund,
            CustomerId = origSale?.SaleType == SaleType.Credit ? saleReturn.CustomerId : null,
            Narration = $"Refund for Return: {saleReturn.ReturnNumber}"
        });

        if (restoredInv > 0m)
        {
            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = invAcc.Id,
                DebitAmount = restoredInv,
                CreditAmount = 0m,
                Narration = $"Stock Restored: {saleReturn.ReturnNumber}"
            });

            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = cogsAcc.Id,
                DebitAmount = 0m,
                CreditAmount = restoredInv,
                Narration = $"COGS Restored: {saleReturn.ReturnNumber}"
            });
        }

        await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
    }

    public async Task PostSaleReturnReversalAsync(
        object dbContext,
        SaleReturn saleReturn,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");

        var orig = await context.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.SaleReturn &&
                                      j.SourceDocumentId == saleReturn.Id &&
                                      j.PostingRole == JournalPostingRole.Primary &&
                                      j.Status == JournalEntryStatus.Posted, cancellationToken);

        if (orig == null) return;

        var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        var revDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        await CheckLockDateAsync(revDate, cancellationToken);

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);
        var revJ = CreateReversalJournal(orig, revNum, revDate, serverUtc, reason);

        await PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
    }

    public async Task PostPurchaseInvoiceJournalAsync(
        object dbContext,
        PurchaseInvoice invoice,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");
        if (state == AccountingSetupState.Initializing)
            throw new ValidationException("Accounting is currently initializing; retry transaction shortly.");

        if (invoice.TaxAmount != 0m)
            throw new ValidationException($"Tax amount must be zero for accounting posting (Tax: {invoice.TaxAmount}).");

        await CheckLockDateAsync(invoice.InvoiceDate, cancellationToken);

        var invAcc = await GetRequiredAccountAsync(context, SystemAccountType.Inventory, cancellationToken);
        var apAcc = await GetRequiredAccountAsync(context, SystemAccountType.AccountsPayableControl, cancellationToken);
        var pDiscAcc = await GetRequiredAccountAsync(context, SystemAccountType.PurchaseDiscount, cancellationToken);

        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, invoice.InvoiceDate.Year, cancellationToken);

        var journal = new JournalEntry
        {
            EntryNumber = jNum,
            BusinessDate = invoice.InvoiceDate,
            PostedAtUtc = invoice.PostedAtUtc ?? DateTime.UtcNow,
            Narration = $"Purchase Invoice: {invoice.InvoiceNumber}",
            SourceDocumentType = JournalSourceDocumentType.PurchaseInvoice,
            SourceDocumentId = invoice.Id,
            SourceDocumentNumber = invoice.InvoiceNumber,
            PostingRole = JournalPostingRole.Primary,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid()
        };

        // Dr Inventory (GrossTotal)
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = invAcc.Id,
            DebitAmount = invoice.GrossTotal,
            CreditAmount = 0m,
            Narration = $"Purchase Inventory: {invoice.InvoiceNumber}"
        });

        // Cr AccountsPayableControl (NetTotal)
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = apAcc.Id,
            DebitAmount = 0m,
            CreditAmount = invoice.NetTotal,
            SupplierId = invoice.SupplierId,
            Narration = $"Purchase Payable: {invoice.InvoiceNumber}"
        });

        // Cr PurchaseDiscount (DiscountAmount if > 0)
        if (invoice.DiscountAmount > 0m)
        {
            journal.Lines.Add(new JournalEntryLine
            {
                AccountId = pDiscAcc.Id,
                DebitAmount = 0m,
                CreditAmount = invoice.DiscountAmount,
                Narration = $"Purchase Discount: {invoice.InvoiceNumber}"
            });
        }

        await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
    }

    public async Task PostPurchaseInvoiceReversalAsync(
        object dbContext,
        PurchaseInvoice invoice,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");

        var orig = await context.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PurchaseInvoice &&
                                      j.SourceDocumentId == invoice.Id &&
                                      j.PostingRole == JournalPostingRole.Primary &&
                                      j.Status == JournalEntryStatus.Posted, cancellationToken);

        if (orig == null) return;

        var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        var revDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        await CheckLockDateAsync(revDate, cancellationToken);

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);
        var revJ = CreateReversalJournal(orig, revNum, revDate, serverUtc, reason);

        await PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
    }

    public async Task PostPurchaseReturnJournalAsync(
        object dbContext,
        PurchaseReturn purchaseReturn,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");
        if (state == AccountingSetupState.Initializing)
            throw new ValidationException("Accounting is currently initializing; retry transaction shortly.");

        await CheckLockDateAsync(purchaseReturn.ReturnDate, cancellationToken);

        var apAcc = await GetRequiredAccountAsync(context, SystemAccountType.AccountsPayableControl, cancellationToken);
        var invAcc = await GetRequiredAccountAsync(context, SystemAccountType.Inventory, cancellationToken);

        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, purchaseReturn.ReturnDate.Year, cancellationToken);

        var journal = new JournalEntry
        {
            EntryNumber = jNum,
            BusinessDate = purchaseReturn.ReturnDate,
            PostedAtUtc = purchaseReturn.PostedAtUtc ?? DateTime.UtcNow,
            Narration = $"Purchase Return: {purchaseReturn.ReturnNumber}",
            SourceDocumentType = JournalSourceDocumentType.PurchaseReturn,
            SourceDocumentId = purchaseReturn.Id,
            SourceDocumentNumber = purchaseReturn.ReturnNumber,
            PostingRole = JournalPostingRole.Primary,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid()
        };

        // Dr AP
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = apAcc.Id,
            DebitAmount = purchaseReturn.TotalAmount,
            CreditAmount = 0m,
            SupplierId = purchaseReturn.SupplierId,
            Narration = $"Purchase Return: {purchaseReturn.ReturnNumber}"
        });

        // Cr Inventory
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = invAcc.Id,
            DebitAmount = 0m,
            CreditAmount = purchaseReturn.TotalAmount,
            Narration = $"Purchase Return: {purchaseReturn.ReturnNumber}"
        });

        await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
    }

    public async Task PostPurchaseReturnReversalAsync(
        object dbContext,
        PurchaseReturn purchaseReturn,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");

        var orig = await context.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PurchaseReturn &&
                                      j.SourceDocumentId == purchaseReturn.Id &&
                                      j.PostingRole == JournalPostingRole.Primary &&
                                      j.Status == JournalEntryStatus.Posted, cancellationToken);

        if (orig == null) return;

        var serverUtc = await _businessClock.GetServerUtcNowAsync(cancellationToken);
        var revDate = await _businessClock.ToBusinessDateAsync(serverUtc, cancellationToken);
        await CheckLockDateAsync(revDate, cancellationToken);

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);
        var revJ = CreateReversalJournal(orig, revNum, revDate, serverUtc, reason);

        await PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
    }

    public async Task PostOpeningStockJournalAsync(
        object dbContext,
        StockMovement movement,
        CancellationToken cancellationToken = default)
    {
        var context = (AppDbContext)dbContext;
        var state = await GetSetupStateAsync(context, cancellationToken);
        if (state == AccountingSetupState.NotConfigured) return;
        if (state == AccountingSetupState.InitializationFailed)
            throw new ValidationException("Accounting initialization failed; resolve before posting transactions.");
        if (state == AccountingSetupState.Initializing)
            throw new ValidationException("Accounting is currently initializing; retry transaction shortly.");

        var businessDate = DateOnly.FromDateTime(movement.TransactionDateUtc);
        await CheckLockDateAsync(businessDate, cancellationToken);

        var invAcc = await GetRequiredAccountAsync(context, SystemAccountType.Inventory, cancellationToken);
        var eqAcc = await GetRequiredAccountAsync(context, SystemAccountType.OpeningBalanceEquity, cancellationToken);

        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, businessDate.Year, cancellationToken);

        var journal = new JournalEntry
        {
            EntryNumber = jNum,
            BusinessDate = businessDate,
            PostedAtUtc = movement.TransactionDateUtc,
            Narration = $"Opening Stock Entry: {movement.ReferenceDocumentNumber}",
            SourceDocumentType = JournalSourceDocumentType.OpeningBalance,
            SourceDocumentId = movement.Id,
            SourceDocumentNumber = movement.ReferenceDocumentNumber,
            PostingRole = JournalPostingRole.Primary,
            Status = JournalEntryStatus.Draft,
            OperationId = Guid.NewGuid()
        };

        decimal val = movement.InventoryValueDelta;

        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = invAcc.Id,
            DebitAmount = val,
            CreditAmount = 0m,
            Narration = "Opening Stock Inventory"
        });

        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = eqAcc.Id,
            DebitAmount = 0m,
            CreditAmount = val,
            Narration = "Opening Stock Equity"
        });

        await PostTwoPhaseJournalAsync(context, journal, cancellationToken);
    }
}
