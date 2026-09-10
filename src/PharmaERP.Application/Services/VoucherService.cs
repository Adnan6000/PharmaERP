using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class VoucherService : IVoucherService
{
    private readonly IVoucherRepository _voucherRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IAccountingTransactionWriter _transactionWriter;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly IAccountingConfigService _configService;

    public VoucherService(
        IVoucherRepository voucherRepository,
        IAccountRepository accountRepository,
        IAccountingTransactionWriter transactionWriter,
        IDocumentNumberGenerator documentNumberGenerator,
        IAccountingConfigService configService)
    {
        _voucherRepository = voucherRepository;
        _accountRepository = accountRepository;
        _transactionWriter = transactionWriter;
        _documentNumberGenerator = documentNumberGenerator;
        _configService = configService;
    }

    public async Task<ReceiptVoucherDto> CreateReceiptVoucherAsync(ReceiptVoucherCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Amount <= 0)
            throw new ValidationException("Receipt voucher amount must be greater than zero.");

        var cashOrBank = await _accountRepository.GetByIdAsync(dto.CashOrBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Cash/Bank account with ID {dto.CashOrBankAccountId} was not found.");

        if (!cashOrBank.IsActive)
            throw new ValidationException($"Cash/Bank account '{cashOrBank.Name}' is inactive.");

        if (!cashOrBank.AllowPosting)
            throw new ValidationException($"Account '{cashOrBank.Name}' is a group account and cannot accept direct postings.");

        if (!cashOrBank.IsCashAccount && !cashOrBank.IsBankAccount)
            throw new ValidationException($"Account '{cashOrBank.Name}' is neither a cash account nor a bank account.");

        var offset = await _accountRepository.GetByIdAsync(dto.OffsetAccountId, cancellationToken)
            ?? throw new NotFoundException($"Offset account with ID {dto.OffsetAccountId} was not found.");

        if (!offset.IsActive)
            throw new ValidationException($"Offset account '{offset.Name}' is inactive.");

        if (!offset.AllowPosting)
            throw new ValidationException($"Offset account '{offset.Name}' is a group account and cannot accept direct postings.");

        if (dto.CashOrBankAccountId == dto.OffsetAccountId)
            throw new ValidationException("Cash/Bank account and Offset account cannot be the same account.");

        if (offset.SystemAccountType == SystemAccountType.Inventory || offset.SystemAccountType == SystemAccountType.CostOfGoodsSold)
            throw new ValidationException("Receipt vouchers cannot post directly to Merchandise Inventory or Cost of Goods Sold.");

        if (offset.SystemAccountType == SystemAccountType.AccountsReceivableControl)
        {
            if (!dto.CustomerId.HasValue)
                throw new ValidationException("Customer is required when posting to Accounts Receivable Control.");
        }
        else
        {
            if (dto.CustomerId.HasValue)
                throw new ValidationException("Customer dimension is only permitted on Accounts Receivable Control accounts.");
        }

        string vNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.ReceiptVoucher, cancellationToken: cancellationToken);
        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);

        return await _transactionWriter.CreateAndPostReceiptVoucherAsync(vNum, jNum, dto, cancellationToken);
    }

    public async Task<ReceiptVoucherDto> CancelReceiptVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);
        return await _transactionWriter.CancelReceiptVoucherAsync(id, revNum, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<ReceiptVoucherDto> GetReceiptVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var v = await _voucherRepository.GetReceiptVoucherByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Receipt voucher with ID {id} was not found.");

        return new ReceiptVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            CashOrBankAccountId = v.CashOrBankAccountId,
            CashOrBankAccountName = v.CashOrBankAccount.Name,
            OffsetAccountId = v.OffsetAccountId,
            OffsetAccountName = v.OffsetAccount.Name,
            CustomerId = v.CustomerId,
            CustomerName = v.Customer?.Name,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        };
    }

    public async Task<PaymentVoucherDto> CreatePaymentVoucherAsync(PaymentVoucherCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Amount <= 0)
            throw new ValidationException("Payment voucher amount must be greater than zero.");

        var cashOrBank = await _accountRepository.GetByIdAsync(dto.CashOrBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Cash/Bank account with ID {dto.CashOrBankAccountId} was not found.");

        if (!cashOrBank.IsActive)
            throw new ValidationException($"Cash/Bank account '{cashOrBank.Name}' is inactive.");

        if (!cashOrBank.AllowPosting)
            throw new ValidationException($"Account '{cashOrBank.Name}' is a group account and cannot accept direct postings.");

        if (!cashOrBank.IsCashAccount && !cashOrBank.IsBankAccount)
            throw new ValidationException($"Account '{cashOrBank.Name}' is neither a cash account nor a bank account.");

        var offset = await _accountRepository.GetByIdAsync(dto.OffsetAccountId, cancellationToken)
            ?? throw new NotFoundException($"Offset account with ID {dto.OffsetAccountId} was not found.");

        if (!offset.IsActive)
            throw new ValidationException($"Offset account '{offset.Name}' is inactive.");

        if (!offset.AllowPosting)
            throw new ValidationException($"Offset account '{offset.Name}' is a group account and cannot accept direct postings.");

        if (dto.CashOrBankAccountId == dto.OffsetAccountId)
            throw new ValidationException("Cash/Bank account and Offset account cannot be the same account.");

        if (offset.SystemAccountType == SystemAccountType.Inventory || offset.SystemAccountType == SystemAccountType.CostOfGoodsSold)
            throw new ValidationException("Payment vouchers cannot post directly to Merchandise Inventory or Cost of Goods Sold.");

        if (offset.SystemAccountType == SystemAccountType.AccountsPayableControl)
        {
            if (!dto.SupplierId.HasValue)
                throw new ValidationException("Supplier is required when posting to Accounts Payable Control.");
        }
        else
        {
            if (dto.SupplierId.HasValue)
                throw new ValidationException("Supplier dimension is only permitted on Accounts Payable Control accounts.");
        }

        string vNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.PaymentVoucher, cancellationToken: cancellationToken);
        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);

        return await _transactionWriter.CreateAndPostPaymentVoucherAsync(vNum, jNum, dto, cancellationToken);
    }

    public async Task<PaymentVoucherDto> CancelPaymentVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);
        return await _transactionWriter.CancelPaymentVoucherAsync(id, revNum, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<PaymentVoucherDto> GetPaymentVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var v = await _voucherRepository.GetPaymentVoucherByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Payment voucher with ID {id} was not found.");

        return new PaymentVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            CashOrBankAccountId = v.CashOrBankAccountId,
            CashOrBankAccountName = v.CashOrBankAccount.Name,
            OffsetAccountId = v.OffsetAccountId,
            OffsetAccountName = v.OffsetAccount.Name,
            SupplierId = v.SupplierId,
            SupplierName = v.Supplier?.Name,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        };
    }

    public async Task<JournalVoucherDto> CreateJournalVoucherAsync(JournalVoucherCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Lines == null || dto.Lines.Count < 2)
            throw new ValidationException("Journal voucher must contain at least two lines.");

        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var line in dto.Lines)
        {
            if (line.DebitAmount < 0 || line.CreditAmount < 0)
                throw new ValidationException("Debit and Credit amounts cannot be negative.");

            if ((line.DebitAmount > 0 && line.CreditAmount > 0) || (line.DebitAmount == 0 && line.CreditAmount == 0))
                throw new ValidationException("Each journal line must specify either a Debit amount or a Credit amount, but not both.");

            if (line.CustomerId.HasValue && line.SupplierId.HasValue)
                throw new ValidationException("A journal line cannot reference both a customer and a supplier simultaneously.");

            var acc = await _accountRepository.GetByIdAsync(line.AccountId, cancellationToken)
                ?? throw new NotFoundException($"Account with ID {line.AccountId} was not found.");

            if (!acc.IsActive)
                throw new ValidationException($"Account '{acc.Name}' is inactive.");

            if (!acc.AllowPosting)
                throw new ValidationException($"Account '{acc.Name}' is a group account and cannot accept direct postings.");

            if (acc.SystemAccountType == SystemAccountType.Inventory || acc.SystemAccountType == SystemAccountType.CostOfGoodsSold)
                throw new ValidationException($"Manual journal vouchers cannot directly debit or credit system account '{acc.Name}'.");

            if (acc.SystemAccountType == SystemAccountType.AccountsReceivableControl)
            {
                if (!line.CustomerId.HasValue)
                    throw new ValidationException($"Customer is required on line for Accounts Receivable Control '{acc.Name}'.");
            }
            else if (acc.SystemAccountType == SystemAccountType.AccountsPayableControl)
            {
                if (!line.SupplierId.HasValue)
                    throw new ValidationException($"Supplier is required on line for Accounts Payable Control '{acc.Name}'.");
            }
            else
            {
                if (line.CustomerId.HasValue || line.SupplierId.HasValue)
                    throw new ValidationException($"Party dimensions are not permitted on account '{acc.Name}'.");
            }

            totalDebit += line.DebitAmount;
            totalCredit += line.CreditAmount;
        }

        if (totalDebit != totalCredit)
            throw new ValidationException($"This voucher is not balanced. Total Debit ({totalDebit:F2}) must equal Total Credit ({totalCredit:F2}).");

        string vNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalVoucher, cancellationToken: cancellationToken);
        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);

        return await _transactionWriter.CreateAndPostJournalVoucherAsync(vNum, jNum, dto, cancellationToken);
    }

    public async Task<JournalVoucherDto> CancelJournalVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);
        return await _transactionWriter.CancelJournalVoucherAsync(id, revNum, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<JournalVoucherDto> GetJournalVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var v = await _voucherRepository.GetJournalVoucherByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Journal voucher with ID {id} was not found.");

        return new JournalVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            Reference = v.Reference,
            Narration = v.Narration,
            TotalAmount = v.TotalAmount,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        };
    }

    public async Task<OpeningBalanceVoucherDto> CreateOpeningBalanceVoucherAsync(OpeningBalanceVoucherCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Lines == null || dto.Lines.Count == 0)
            throw new ValidationException("Opening balance entry must contain at least one line.");

        decimal totalUserDebits = 0m;
        decimal totalUserCredits = 0m;

        foreach (var line in dto.Lines)
        {
            if (line.DebitAmount < 0 || line.CreditAmount < 0)
                throw new ValidationException("Debit and Credit amounts cannot be negative.");

            if ((line.DebitAmount > 0 && line.CreditAmount > 0) || (line.DebitAmount == 0 && line.CreditAmount == 0))
                throw new ValidationException("Each line must specify either Debit or Credit, but not both.");

            if (line.CustomerId.HasValue && line.SupplierId.HasValue)
                throw new ValidationException("A line cannot reference both a customer and a supplier simultaneously.");

            var acc = await _accountRepository.GetByIdAsync(line.AccountId, cancellationToken)
                ?? throw new NotFoundException($"Account with ID {line.AccountId} was not found.");

            if (!acc.IsActive)
                throw new ValidationException($"Account '{acc.Name}' is inactive.");

            if (!acc.AllowPosting)
                throw new ValidationException($"Account '{acc.Name}' is a group account and cannot accept direct postings.");

            if (acc.SystemAccountType == SystemAccountType.Inventory || acc.SystemAccountType == SystemAccountType.CostOfGoodsSold)
                throw new ValidationException("Opening Balance Voucher cannot directly debit or credit Merchandise Inventory or COGS. Opening inventory is recorded through Opening Stock.");

            if (acc.SystemAccountType == SystemAccountType.OpeningBalanceEquity)
                throw new ValidationException("Opening Balance Equity is system-balanced and cannot be manually selected as a line item.");

            if (acc.SystemAccountType == SystemAccountType.AccountsReceivableControl && !line.CustomerId.HasValue)
                throw new ValidationException($"Customer is required on line for Accounts Receivable Control '{acc.Name}'.");

            if (acc.SystemAccountType == SystemAccountType.AccountsPayableControl && !line.SupplierId.HasValue)
                throw new ValidationException($"Supplier is required on line for Accounts Payable Control '{acc.Name}'.");

            totalUserDebits += line.DebitAmount;
            totalUserCredits += line.CreditAmount;
        }

        string vNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.OpeningBalanceVoucher, cancellationToken: cancellationToken);
        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);

        return await _transactionWriter.CreateAndPostOpeningBalanceVoucherAsync(vNum, jNum, dto, cancellationToken);
    }

    public async Task<OpeningBalanceVoucherDto> CancelOpeningBalanceVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);
        return await _transactionWriter.CancelOpeningBalanceVoucherAsync(id, revNum, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<OpeningBalanceVoucherDto> GetOpeningBalanceVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var v = await _voucherRepository.GetOpeningBalanceVoucherByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Opening balance voucher with ID {id} was not found.");

        return new OpeningBalanceVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        };
    }

    public async Task<ContraVoucherDto> CreateContraVoucherAsync(ContraVoucherCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Amount <= 0)
            throw new ValidationException("Contra voucher amount must be greater than zero.");

        if (dto.SourceAccountId == dto.DestinationAccountId)
            throw new ValidationException("Source and Destination accounts cannot be the same account.");

        var srcAcc = await _accountRepository.GetByIdAsync(dto.SourceAccountId, cancellationToken)
            ?? throw new NotFoundException($"Source account with ID {dto.SourceAccountId} was not found.");

        if (!srcAcc.IsActive)
            throw new ValidationException($"Source account '{srcAcc.Name}' is inactive.");

        if (!srcAcc.AllowPosting)
            throw new ValidationException($"Source account '{srcAcc.Name}' is a group account and cannot accept direct postings.");

        if (!srcAcc.IsCashAccount && !srcAcc.IsBankAccount)
            throw new ValidationException($"Source account '{srcAcc.Name}' must be a Cash or Bank account.");

        var dstAcc = await _accountRepository.GetByIdAsync(dto.DestinationAccountId, cancellationToken)
            ?? throw new NotFoundException($"Destination account with ID {dto.DestinationAccountId} was not found.");

        if (!dstAcc.IsActive)
            throw new ValidationException($"Destination account '{dstAcc.Name}' is inactive.");

        if (!dstAcc.AllowPosting)
            throw new ValidationException($"Destination account '{dstAcc.Name}' is a group account and cannot accept direct postings.");

        if (!dstAcc.IsCashAccount && !dstAcc.IsBankAccount)
            throw new ValidationException($"Destination account '{dstAcc.Name}' must be a Cash or Bank account.");

        var lockDate = await _configService.GetAccountingLockDateAsync(cancellationToken);
        var businessDate = dto.BusinessDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (lockDate.HasValue && businessDate <= lockDate.Value)
            throw new ValidationException($"Accounting period is locked up to {lockDate:yyyy-MM-dd}. Transactions on or before this date are not permitted.");

        string vNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.ContraVoucher, cancellationToken: cancellationToken);
        string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);

        return await _transactionWriter.CreateAndPostContraVoucherAsync(vNum, jNum, dto, cancellationToken);
    }

    public async Task<ContraVoucherDto> CancelContraVoucherAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ValidationException("Cancellation reason is required.");

        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, cancellationToken: cancellationToken);
        return await _transactionWriter.CancelContraVoucherAsync(id, revNum, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<ContraVoucherDto> GetContraVoucherByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var v = await _voucherRepository.GetContraVoucherByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Contra voucher with ID {id} was not found.");

        return new ContraVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            SourceAccountId = v.SourceAccountId,
            SourceAccountName = v.SourceAccount?.Name ?? string.Empty,
            DestinationAccountId = v.DestinationAccountId,
            DestinationAccountName = v.DestinationAccount?.Name ?? string.Empty,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        };
    }

    public async Task<List<ReceiptVoucherDto>> GetReceiptVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        var list = await _voucherRepository.GetReceiptVouchersAsync(fromDate, toDate, cancellationToken);
        return list.Select(v => new ReceiptVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            CashOrBankAccountId = v.CashOrBankAccountId,
            CashOrBankAccountName = v.CashOrBankAccount?.Name ?? string.Empty,
            OffsetAccountId = v.OffsetAccountId,
            OffsetAccountName = v.OffsetAccount?.Name ?? string.Empty,
            CustomerId = v.CustomerId,
            CustomerName = v.Customer?.Name,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        }).ToList();
    }

    public async Task<List<PaymentVoucherDto>> GetPaymentVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        var list = await _voucherRepository.GetPaymentVouchersAsync(fromDate, toDate, cancellationToken);
        return list.Select(v => new PaymentVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            CashOrBankAccountId = v.CashOrBankAccountId,
            CashOrBankAccountName = v.CashOrBankAccount?.Name ?? string.Empty,
            OffsetAccountId = v.OffsetAccountId,
            OffsetAccountName = v.OffsetAccount?.Name ?? string.Empty,
            SupplierId = v.SupplierId,
            SupplierName = v.Supplier?.Name,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        }).ToList();
    }

    public async Task<List<JournalVoucherDto>> GetJournalVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        var list = await _voucherRepository.GetJournalVouchersAsync(fromDate, toDate, cancellationToken);
        return list.Select(v => new JournalVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            Reference = v.Reference,
            Narration = v.Narration,
            TotalAmount = v.TotalAmount,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        }).ToList();
    }

    public async Task<List<OpeningBalanceVoucherDto>> GetOpeningBalanceVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        var list = await _voucherRepository.GetOpeningBalanceVouchersAsync(fromDate, toDate, cancellationToken);
        return list.Select(v => new OpeningBalanceVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        }).ToList();
    }

    public async Task<List<ContraVoucherDto>> GetContraVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        var list = await _voucherRepository.GetContraVouchersAsync(fromDate, toDate, cancellationToken);
        return list.Select(v => new ContraVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            BusinessDate = v.BusinessDate,
            PostedAtUtc = v.PostedAtUtc,
            SourceAccountId = v.SourceAccountId,
            SourceAccountName = v.SourceAccount?.Name ?? string.Empty,
            DestinationAccountId = v.DestinationAccountId,
            DestinationAccountName = v.DestinationAccount?.Name ?? string.Empty,
            Amount = v.Amount,
            Reference = v.Reference,
            Narration = v.Narration,
            Status = v.Status,
            OperationId = v.OperationId,
            CancelledAtUtc = v.CancelledAtUtc,
            CancellationReason = v.CancellationReason
        }).ToList();
    }
}
