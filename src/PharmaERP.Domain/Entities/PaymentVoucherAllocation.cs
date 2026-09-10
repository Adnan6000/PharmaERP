using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Settlement allocation linking a Payment Voucher to a Purchase Invoice.
/// </summary>
public class PaymentVoucherAllocation : BaseEntity
{
    public int PaymentVoucherId { get; set; }
    public PaymentVoucher PaymentVoucher { get; set; } = null!;

    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

    public decimal AllocatedAmount { get; set; }

    public DateTime AllocatedAtUtc { get; set; } = DateTime.UtcNow;

    public AllocationStatus Status { get; set; } = AllocationStatus.Active;

    public DateTime? VoidedAtUtc { get; set; }

    public string? VoidReason { get; set; }
}

