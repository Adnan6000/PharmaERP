using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public enum PurchaseInvoiceStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

/// <summary>
/// Vendor purchase invoice header representing goods received from suppliers.
/// </summary>
public class PurchaseInvoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public string? SupplierInvoiceNumber { get; set; }

    public string? NormalizedSupplierInvoiceNumber { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateOnly InvoiceDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Remarks { get; set; }

    public decimal GrossTotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal NetTotal { get; set; }

    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();

    public ICollection<PurchaseReturn> Returns { get; set; } = new List<PurchaseReturn>();
    public ICollection<PaymentVoucherAllocation> Allocations { get; set; } = new List<PaymentVoucherAllocation>();
}

