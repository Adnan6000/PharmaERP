using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Settlement allocation linking a Receipt Voucher to a Sale Invoice.
/// </summary>
public class ReceiptVoucherAllocation : BaseEntity
{
    public int ReceiptVoucherId { get; set; }
    public ReceiptVoucher ReceiptVoucher { get; set; } = null!;

    public int SaleInvoiceId { get; set; }
    public SaleInvoice SaleInvoice { get; set; } = null!;

    public decimal AllocatedAmount { get; set; }

    public DateTime AllocatedAtUtc { get; set; } = DateTime.UtcNow;

    public AllocationStatus Status { get; set; } = AllocationStatus.Active;

    public DateTime? VoidedAtUtc { get; set; }

    public string? VoidReason { get; set; }
}

