using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Receipt Voucher representing customer receipts or miscellaneous cash/bank receipts.
/// Non-circular relation to JournalEntry via SourceDocumentType / SourceDocumentId.
/// </summary>
public class ReceiptVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty; // "RV-2026-000001"

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public int CashOrBankAccountId { get; set; }
    public Account CashOrBankAccount { get; set; } = null!;

    public int OffsetAccountId { get; set; } // AR Control OR Income/Other Account
    public Account OffsetAccount { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }

    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public Guid OperationId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<ReceiptVoucherAllocation> Allocations { get; set; } = new List<ReceiptVoucherAllocation>();
}
