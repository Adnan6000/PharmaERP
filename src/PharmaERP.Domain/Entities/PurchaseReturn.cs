using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public enum PurchaseReturnStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

/// <summary>
/// Purchase return header representing goods returned back to a vendor/supplier.
/// </summary>
public class PurchaseReturn : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;

    public int? OriginalPurchaseInvoiceId { get; set; }
    public PurchaseInvoice? OriginalPurchaseInvoice { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateOnly ReturnDate { get; set; }

    public string? Reason { get; set; }

    public decimal TotalAmount { get; set; }

    public PurchaseReturnStatus Status { get; set; } = PurchaseReturnStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}

