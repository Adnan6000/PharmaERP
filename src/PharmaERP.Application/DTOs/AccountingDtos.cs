using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.DTOs;

public record AccountDto
{
    public int Id { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public int? ParentAccountId { get; init; }
    public string? ParentAccountName { get; init; }
    public bool AllowPosting { get; init; }
    public bool IsControlAccount { get; init; }
    public bool IsCashAccount { get; init; }
    public bool IsBankAccount { get; init; }
    public SystemAccountType? SystemAccountType { get; init; }
    public bool IsActive { get; init; }
    public string? Description { get; init; }
}

public record AccountTreeDto : AccountDto
{
    public List<AccountTreeDto> Children { get; init; } = new();
}

public record AccountCreateDto
{
    public string AccountCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public int? ParentAccountId { get; init; }
    public bool AllowPosting { get; init; } = true;
    public bool IsControlAccount { get; init; }
    public bool IsCashAccount { get; init; }
    public bool IsBankAccount { get; init; }
    public SystemAccountType? SystemAccountType { get; init; }
    public string? Description { get; init; }
}

public record AccountUpdateDto
{
    public string Name { get; init; } = string.Empty;
    public int? ParentAccountId { get; init; }
    public bool AllowPosting { get; init; } = true;
    public bool IsControlAccount { get; init; }
    public bool IsCashAccount { get; init; }
    public bool IsBankAccount { get; init; }
    public bool IsActive { get; init; } = true;
    public string? Description { get; init; }
}

public record JournalEntryLineDto
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? Narration { get; init; }
}

public record JournalEntryDto
{
    public int Id { get; init; }
    public string EntryNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public string? Narration { get; init; }
    public JournalSourceDocumentType SourceDocumentType { get; init; }
    public int? SourceDocumentId { get; init; }
    public string? SourceDocumentNumber { get; init; }
    public JournalPostingRole PostingRole { get; init; }
    public JournalEntryStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public int? ReversesJournalEntryId { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
    public List<JournalEntryLineDto> Lines { get; init; } = new();
}

public record ReceiptVoucherDto
{
    public int Id { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public int CashOrBankAccountId { get; init; }
    public string CashOrBankAccountName { get; init; } = string.Empty;
    public int OffsetAccountId { get; init; }
    public string OffsetAccountName { get; init; } = string.Empty;
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public VoucherStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
}

public record ReceiptVoucherCreateDto
{
    public DateOnly? BusinessDate { get; init; }
    public int CashOrBankAccountId { get; init; }
    public int OffsetAccountId { get; init; }
    public int? CustomerId { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public Guid OperationId { get; init; } = Guid.NewGuid();
}

public record PaymentVoucherDto
{
    public int Id { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public int CashOrBankAccountId { get; init; }
    public string CashOrBankAccountName { get; init; } = string.Empty;
    public int OffsetAccountId { get; init; }
    public string OffsetAccountName { get; init; } = string.Empty;
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public VoucherStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
}

public record PaymentVoucherCreateDto
{
    public DateOnly? BusinessDate { get; init; }
    public int CashOrBankAccountId { get; init; }
    public int OffsetAccountId { get; init; }
    public int? SupplierId { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public Guid OperationId { get; init; } = Guid.NewGuid();
}

public record ContraVoucherDto
{
    public int Id { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public int SourceAccountId { get; init; }
    public string SourceAccountName { get; init; } = string.Empty;
    public int DestinationAccountId { get; init; }
    public string DestinationAccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public VoucherStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
}

public record ContraVoucherCreateDto
{
    public DateOnly? BusinessDate { get; init; }
    public int SourceAccountId { get; init; }
    public int DestinationAccountId { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public Guid OperationId { get; init; } = Guid.NewGuid();
}

public record JournalVoucherLineCreateDto
{
    public int AccountId { get; init; }
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public int? CustomerId { get; init; }
    public int? SupplierId { get; init; }
    public string? Narration { get; init; }
}

public record JournalVoucherCreateDto
{
    public DateOnly? BusinessDate { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public List<JournalVoucherLineCreateDto> Lines { get; init; } = new();
    public Guid OperationId { get; init; } = Guid.NewGuid();
}

public record JournalVoucherDto
{
    public int Id { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public decimal TotalAmount { get; init; }
    public VoucherStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
    public List<JournalEntryLineDto> Lines { get; init; } = new();
}

public record OpeningBalanceLineCreateDto
{
    public int AccountId { get; init; }
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public int? CustomerId { get; init; }
    public int? SupplierId { get; init; }
    public string? Narration { get; init; }
}

public record OpeningBalanceVoucherCreateDto
{
    public DateOnly? BusinessDate { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public List<OpeningBalanceLineCreateDto> Lines { get; init; } = new();
    public Guid OperationId { get; init; } = Guid.NewGuid();
}

public record OpeningBalanceVoucherDto
{
    public int Id { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public VoucherStatus Status { get; init; }
    public Guid OperationId { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancellationReason { get; init; }
    public decimal TotalAmount => Lines.Sum(l => l.DebitAmount);
    public List<JournalEntryLineDto> Lines { get; init; } = new();
}

public record LedgerReportLineDto
{
    public int LineId { get; init; }
    public int JournalEntryId { get; init; }
    public string EntryNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public JournalSourceDocumentType SourceDocumentType { get; init; }
    public string? SourceDocumentNumber { get; init; }
    public string? Narration { get; init; }
    public string? PartyName { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public decimal RunningBalance { get; init; }
    public bool IsReversal { get; init; }
}

public record LedgerReportDto
{
    public int? AccountId { get; init; }
    public string? AccountCode { get; init; }
    public string? AccountName { get; init; }
    public int? PartyId { get; init; }
    public string? PartyName { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public decimal PeriodDebit => TotalDebit;
    public decimal PeriodCredit => TotalCredit;
    public string? CustomerName => PartyName;
    public string? SupplierName => PartyName;
    public decimal ClosingBalance { get; init; }
    public int TotalRows { get; init; }
    public List<LedgerReportLineDto> Lines { get; init; } = new();
}

public record TrialBalanceLineDto
{
    public int AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public bool IsActive { get; init; }
    public decimal OpeningDebit { get; init; }
    public decimal OpeningCredit { get; init; }
    public decimal PeriodDebit { get; init; }
    public decimal PeriodCredit { get; init; }
    public decimal ClosingDebit { get; init; }
    public decimal ClosingCredit { get; init; }
}

public record TrialBalanceReportDto
{
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public decimal TotalOpeningDebit { get; init; }
    public decimal TotalOpeningCredit { get; init; }
    public decimal TotalPeriodDebit { get; init; }
    public decimal TotalPeriodCredit { get; init; }
    public decimal TotalClosingDebit { get; init; }
    public decimal TotalClosingCredit { get; init; }
    public decimal Difference => Math.Abs(TotalClosingDebit - TotalClosingCredit);
    public bool IsBalanced => TotalClosingDebit == TotalClosingCredit;
    public List<TrialBalanceLineDto> Lines { get; init; } = new();
}

public record DayBookLineDto
{
    public int JournalEntryId { get; init; }
    public string EntryNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public JournalSourceDocumentType SourceDocumentType { get; init; }
    public string? SourceDocumentNumber { get; init; }
    public string? Narration { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public bool IsReversal { get; init; }
    public int? ReversesJournalEntryId { get; init; }
    public List<JournalEntryLineDto> Lines { get; init; } = new();
}

public record DayBookReportDto
{
    public DateOnly BusinessDate { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public List<DayBookLineDto> Entries { get; init; } = new();
}

public record AccountingStatusDto
{
    public AccountingSetupState State { get; init; }
    public DateOnly? AccountingLockDate { get; init; }
    public bool HasAccounts { get; init; }
    public bool HasUnaccountedHistoricalDocuments { get; init; }
    public int UnaccountedHistoricalCount { get; init; }
}

public record HistoricalInitializationResultDto
{
    public bool Success { get; init; }
    public AccountingSetupState FinalState { get; init; }
    public int OpeningStocksProcessed { get; init; }
    public int PurchasesProcessed { get; init; }
    public int PurchaseReturnsProcessed { get; init; }
    public int SalesProcessed { get; init; }
    public int SalesReturnsProcessed { get; init; }
    public int TotalJournalsCreated { get; init; }
    public int TotalJournalsSkipped { get; init; }
    public List<string> ErrorMessages { get; init; } = new();
    public ReconciliationReportDto? ReconciliationReport { get; init; }
}

public record ReconciliationReportDto
{
    public bool AllMatched { get; init; }
    public decimal InventoryGlBalance { get; init; }
    public decimal PhysicalStockValuation { get; init; }
    public decimal InventoryDifference => InventoryGlBalance - PhysicalStockValuation;
    public bool InventoryMatched => InventoryDifference == 0m;

    public decimal ArControlBalance { get; init; }
    public decimal CustomerSubledgersTotal { get; init; }
    public decimal ArDifference => ArControlBalance - CustomerSubledgersTotal;
    public bool ArMatched => ArDifference == 0m;

    public decimal ApControlBalance { get; init; }
    public decimal SupplierSubledgersTotal { get; init; }
    public decimal ApDifference => ApControlBalance - SupplierSubledgersTotal;
    public bool ApMatched => ApDifference == 0m;

    public decimal TrialBalanceDebit { get; init; }
    public decimal TrialBalanceCredit { get; init; }
    public decimal TrialBalanceDifference => TrialBalanceDebit - TrialBalanceCredit;
    public bool TrialBalanceMatched => TrialBalanceDifference == 0m;

    public string InventoryStatusText => InventoryMatched ? "✅ Reconciled (Matched)" : "⚠️ Difference Detected";
    public string ArStatusText => ArMatched ? "✅ Reconciled (Matched)" : "⚠️ Difference Detected";
    public string ApStatusText => ApMatched ? "✅ Reconciled (Matched)" : "⚠️ Difference Detected";
    public string TrialBalanceStatusText => TrialBalanceMatched ? "✅ Reconciled (Balanced)" : "⚠️ Difference Detected";
}

public record FinancialStatementLineDto
{
    public int AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public record ProfitAndLossStatementDto
{
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public List<FinancialStatementLineDto> RevenueLines { get; init; } = new();
    public decimal TotalRevenue { get; init; }
    public List<FinancialStatementLineDto> ExpenseLines { get; init; } = new();
    public decimal TotalExpense { get; init; }
    public decimal NetProfitOrLoss { get; init; }
    public bool IsProfit { get; init; }
}

public record BalanceSheetStatementDto
{
    public DateOnly AsOfDate { get; init; }
    public List<FinancialStatementLineDto> AssetLines { get; init; } = new();
    public decimal TotalAssets { get; init; }
    public List<FinancialStatementLineDto> LiabilityLines { get; init; } = new();
    public decimal TotalLiabilities { get; init; }
    public List<FinancialStatementLineDto> EquityLines { get; init; } = new();
    public decimal TotalEquity { get; init; }
    public decimal CurrentPeriodEarnings { get; init; }
    public decimal TotalLiabilitiesAndEquity { get; init; }
    public decimal Difference { get; init; }
    public bool IsBalanced { get; init; }
}

public record OpenSaleInvoiceDto
{
    public int InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal NetTotal { get; init; }
    public decimal TotalAllocated { get; init; }
    public decimal OutstandingBalance => Math.Max(0m, NetTotal - TotalAllocated);
    public string Status => OutstandingBalance <= 0m ? "Paid" : (TotalAllocated > 0m ? "PartiallyPaid" : "Unpaid");
}

public record OpenPurchaseInvoiceDto
{
    public int InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string? SupplierInvoiceNumber { get; init; }
    public DateOnly InvoiceDate { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public decimal NetTotal { get; init; }
    public decimal TotalAllocated { get; init; }
    public decimal OutstandingBalance => Math.Max(0m, NetTotal - TotalAllocated);
    public string Status => OutstandingBalance <= 0m ? "Paid" : (TotalAllocated > 0m ? "PartiallyPaid" : "Unpaid");
}

public record ReceiptVoucherAllocationDto
{
    public int Id { get; init; }
    public int ReceiptVoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public int SaleInvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public decimal AllocatedAmount { get; init; }
    public DateTime AllocatedAtUtc { get; init; }
    public AllocationStatus Status { get; init; } = AllocationStatus.Active;
    public DateTime? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public record PaymentVoucherAllocationDto
{
    public int Id { get; init; }
    public int PaymentVoucherId { get; init; }
    public string VoucherNumber { get; init; } = string.Empty;
    public int PurchaseInvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public decimal AllocatedAmount { get; init; }
    public DateTime AllocatedAtUtc { get; init; }
    public AllocationStatus Status { get; init; } = AllocationStatus.Active;
    public DateTime? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public record ReceiptAllocationCreateDto
{
    public int ReceiptVoucherId { get; init; }
    public int SaleInvoiceId { get; init; }
    public decimal AllocatedAmount { get; init; }
}

public record PaymentAllocationCreateDto
{
    public int PaymentVoucherId { get; init; }
    public int PurchaseInvoiceId { get; init; }
    public decimal AllocatedAmount { get; init; }
}

public record VoidAllocationDto
{
    public int AllocationId { get; init; }
    public string VoidReason { get; init; } = string.Empty;
}


