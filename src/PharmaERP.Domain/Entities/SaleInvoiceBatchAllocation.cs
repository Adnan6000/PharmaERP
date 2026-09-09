using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public class SaleInvoiceBatchAllocation : BaseEntity
{
    public int SaleInvoiceItemId { get; set; }
    public SaleInvoiceItem SaleInvoiceItem { get; set; } = null!;

    public int ProductBatchId { get; set; }
    public ProductBatch ProductBatch { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// Historical pre-sale average purchase cost snapshot at the moment of sale.
    /// </summary>
    public decimal InventoryCostRate { get; set; }

    /// <summary>
    /// Authoritative total monetary inventory value removed from the batch (OldInventoryValue - NewInventoryValue).
    /// </summary>
    public decimal InventoryValueConsumed { get; set; }

    public string BatchNumberSnapshot { get; set; } = string.Empty;

    public DateOnly ExpiryDateSnapshot { get; set; }

    public decimal ReturnedQuantity { get; set; }

    /// <summary>
    /// Cumulative inventory value restored across all returns against this allocation.
    /// Invariant: ReturnedInventoryValue <= InventoryValueConsumed.
    /// </summary>
    public decimal ReturnedInventoryValue { get; set; }
}

