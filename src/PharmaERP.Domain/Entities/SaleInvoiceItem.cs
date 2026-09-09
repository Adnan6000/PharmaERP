using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public class SaleInvoiceItem : BaseEntity
{
    public int SaleInvoiceId { get; set; }
    public SaleInvoice SaleInvoice { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductCodeSnapshot { get; set; } = string.Empty;

    public string ProductNameSnapshot { get; set; } = string.Empty;

    public string UnitSnapshot { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitSalePrice { get; set; }

    public decimal GrossAmount { get; set; }

    public decimal LineDiscountAmount { get; set; }

    public decimal InvoiceDiscountAllocated { get; set; }

    public decimal TaxAllocated { get; set; }

    public decimal NetLineAmount { get; set; }

    public decimal ReturnedQuantity { get; set; }

    /// <summary>
    /// Cumulative refunded amount across all returns against this item, atomically guarded.
    /// </summary>
    public decimal RefundedAmount { get; set; }

    public ICollection<SaleInvoiceBatchAllocation> BatchAllocations { get; set; } = new List<SaleInvoiceBatchAllocation>();
}

