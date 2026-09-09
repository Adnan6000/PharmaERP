using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Line item of a purchase invoice tracking product batch details, quantities, rates, and returns.
/// </summary>
public class PurchaseInvoiceItem : BaseEntity
{
    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProductBatchId { get; set; }
    public ProductBatch ProductBatch { get; set; } = null!;

    public string BatchNumber { get; set; } = string.Empty;

    public DateOnly ExpiryDate { get; set; }

    public DateOnly? ManufacturingDate { get; set; }

    public decimal Quantity { get; set; }

    public decimal PurchaseRate { get; set; }

    public decimal? SuggestedSaleRate { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    /// <summary>
    /// Cumulative quantity returned against this invoice line across all purchase returns.
    /// Guarded by database atomic check to never exceed Quantity.
    /// </summary>
    public decimal ReturnedQuantity { get; set; }
}

