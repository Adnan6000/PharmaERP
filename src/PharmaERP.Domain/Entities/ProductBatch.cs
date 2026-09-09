using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Physical batch of a medicine product with distinct manufacturing and expiry dates,
/// maintaining exact inventory quantities, valuation, and moving weighted average costs.
/// </summary>
public class ProductBatch : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNumber { get; set; } = string.Empty;

    /// <summary>
    /// Normalized (trimmed and uppercase invariant) representation used for database uniqueness.
    /// </summary>
    public string NormalizedBatchNumber { get; set; } = string.Empty;

    public DateOnly? ManufacturingDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    /// <summary>
    /// Current physical stock quantity on hand in default dispensing units.
    /// </summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Current monetary valuation of this batch (QuantityOnHand * AveragePurchaseCost).
    /// </summary>
    public decimal InventoryValue { get; set; }

    /// <summary>
    /// Weighted moving-average unit purchase cost.
    /// </summary>
    public decimal AveragePurchaseCost { get; set; }

    /// <summary>
    /// Most recent unit purchase rate.
    /// </summary>
    public decimal LastPurchaseCost { get; set; }

    /// <summary>
    /// Suggested sale rate for retail billing.
    /// </summary>
    public decimal? SuggestedSalePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}

