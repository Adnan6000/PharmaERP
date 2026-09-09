using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public class SaleReturnItem : BaseEntity
{
    public int SaleReturnId { get; set; }
    public SaleReturn SaleReturn { get; set; } = null!;

    public int OriginalSaleInvoiceItemId { get; set; }
    public SaleInvoiceItem OriginalSaleInvoiceItem { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// Financial refund amount allocated to this return line.
    /// </summary>
    public decimal RefundAmount { get; set; }

    public ICollection<SaleReturnBatchAllocation> BatchAllocations { get; set; } = new List<SaleReturnBatchAllocation>();
}

