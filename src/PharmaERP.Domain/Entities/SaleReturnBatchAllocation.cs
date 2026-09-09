using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public class SaleReturnBatchAllocation : BaseEntity
{
    public int SaleReturnItemId { get; set; }
    public SaleReturnItem SaleReturnItem { get; set; } = null!;

    public int OriginalSaleInvoiceBatchAllocationId { get; set; }
    public SaleInvoiceBatchAllocation OriginalSaleInvoiceBatchAllocation { get; set; } = null!;

    public int ProductBatchId { get; set; }
    public ProductBatch ProductBatch { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal RestoredInventoryCostRate { get; set; }

    /// <summary>
    /// Exact inventory monetary value restored to this batch.
    /// </summary>
    public decimal RestoredInventoryValue { get; set; }
}

