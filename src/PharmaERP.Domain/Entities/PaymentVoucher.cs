using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Payment Voucher representing supplier payments or expense cash/bank disbursements.
/// </summary>
public class PaymentVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty; // "PV-2026-000001"

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public int CashOrBankAccountId { get; set; }
    public Account CashOrBankAccount { get; set; } = null!;

    public int OffsetAccountId { get; set; } // AP Control OR Expense/Other Account
    public Account OffsetAccount { get; set; } = null!;

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public decimal Amount { get; set; }

    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public Guid OperationId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<PaymentVoucherAllocation> Allocations { get; set; } = new List<PaymentVoucherAllocation>();
}
