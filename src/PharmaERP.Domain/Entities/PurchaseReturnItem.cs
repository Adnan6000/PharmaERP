using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Line item of a purchase return referencing the product, batch, and optionally original purchase invoice line.
/// </summary>
public class PurchaseReturnItem : BaseEntity
{
    public int PurchaseReturnId { get; set; }
    public PurchaseReturn PurchaseReturn { get; set; } = null!;

    public int? OriginalPurchaseInvoiceItemId { get; set; }
    public PurchaseInvoiceItem? OriginalPurchaseInvoiceItem { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProductBatchId { get; set; }
    public ProductBatch ProductBatch { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// Return rate per unit. For linked returns, this is locked to the original purchase rate.
    /// </summary>
    public decimal ReturnRate { get; set; }

    public decimal LineTotal { get; set; }
}

