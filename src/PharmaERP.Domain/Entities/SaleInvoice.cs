using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

public class SaleInvoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid OperationId { get; set; }

    public DateTime InvoiceDateTimeUtc { get; set; } = DateTime.UtcNow;

    public DateOnly BusinessDate { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string CustomerNameSnapshot { get; set; } = string.Empty;

    public SaleType SaleType { get; set; } = SaleType.Cash;

    public string? Reference { get; set; }

    public string? Remarks { get; set; }

    public decimal GrossTotal { get; set; }

    public decimal LineDiscountTotal { get; set; }

    public decimal InvoiceDiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal NetTotal { get; set; }

    public decimal? TenderedAmount { get; set; }

    public decimal? ChangeGiven { get; set; }

    public SaleInvoiceStatus Status { get; set; } = SaleInvoiceStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    // Historical Print Identity Snapshots
    public string? PharmacyNameSnapshot { get; set; }
    public string? PharmacyAddressSnapshot { get; set; }
    public string? PharmacyPhoneSnapshot { get; set; }
    public string? DrugLicenseNoSnapshot { get; set; }
    public string? TaxNumberSnapshot { get; set; }
    public string? ReceiptFooterSnapshot { get; set; }

    public ICollection<SaleInvoiceItem> Items { get; set; } = new List<SaleInvoiceItem>();
}

