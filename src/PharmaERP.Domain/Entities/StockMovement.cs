using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public enum StockMovementType
{
    OpeningStock = 1,           // Inflow  (+)
    Purchase = 2,               // Inflow  (+)
    PurchaseReturn = 3,         // Outflow (-)
    PurchaseReversal = 4,       // Outflow (-) Compensating movement for cancelled purchase
    PurchaseReturnReversal = 5, // Inflow  (+) Compensating movement for cancelled purchase return
    Sale = 6,                   // Outflow (-) [Milestone 4]
    SaleReturn = 7,             // Inflow  (+) [Milestone 4]
    AdjustmentIn = 8,           // Inflow  (+) Physical count surplus
    AdjustmentOut = 9,          // Outflow (-) Physical count deficit / damage
    SaleReversal = 10,          // Inflow  (+) Compensating movement for cancelled sale [Milestone 4]
    SaleReturnReversal = 11     // Outflow (-) Compensating movement for cancelled sales return [Milestone 4]
}

public enum StockReferenceDocumentType
{
    OpeningStock = 1,
    PurchaseInvoice = 2,
    PurchaseReturn = 3,
    StockAdjustment = 4,
    SaleInvoice = 5,
    SaleReturn = 6
}

/// <summary>
/// Immutable, append-only inventory ledger record preserving exact physical quantities,
/// financial transaction rates, and inventory valuation snapshots.
/// </summary>
public class StockMovement : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProductBatchId { get; set; }
    public ProductBatch ProductBatch { get; set; } = null!;

    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Signed quantity change (+ for inflows, - for outflows).
    /// </summary>
    public decimal QuantityDelta { get; set; }

    /// <summary>
    /// Resulting physical batch balance immediately after this movement.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// Financial document rate (e.g. invoice purchase rate or sale rate).
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Valuation cost rate applied for inventory accounting (e.g. weighted average cost).
    /// </summary>
    public decimal InventoryCostRate { get; set; }

    /// <summary>
    /// Signed monetary valuation change (+ for inflows, - for outflows).
    /// </summary>
    public decimal InventoryValueDelta { get; set; }

    /// <summary>
    /// Resulting total batch inventory monetary value immediately after this movement.
    /// </summary>
    public decimal InventoryValueAfter { get; set; }

    public StockReferenceDocumentType ReferenceDocumentType { get; set; }

    public int? ReferenceDocumentId { get; set; }

    public int? ReferenceDocumentItemId { get; set; }

    public string? ReferenceDocumentNumber { get; set; }

    /// <summary>
    /// Foreign key linking this compensating reversal movement to the exact original movement being reversed.
    /// </summary>
    public int? ReversesStockMovementId { get; set; }
    public StockMovement? ReversesStockMovement { get; set; }

    public DateTime TransactionDateUtc { get; set; } = DateTime.UtcNow;

    public string? Remarks { get; set; }
}

