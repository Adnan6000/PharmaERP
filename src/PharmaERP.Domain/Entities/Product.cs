using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Core catalog entity representing a pharmaceutical product.
/// Note: BatchNumber, ExpiryDate, and batch-wise costing are intentionally omitted
/// because batches are managed separately in a dedicated inventory milestone.
/// </summary>
public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? GenericName { get; set; }

    public string? ProductCode { get; set; }

    public string? Barcode { get; set; }

    /// <summary>
    /// Default catalog purchase price. Actual purchase prices belong to purchase invoice lines / batches.
    /// </summary>
    public decimal DefaultPurchasePrice { get; set; }

    /// <summary>
    /// Default catalog selling price. Actual selling prices belong to sales invoice lines / batches.
    /// </summary>
    public decimal DefaultSalePrice { get; set; }

    public bool IsActive { get; set; } = true;

    // Foreign Keys and Navigation Properties
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? ManufacturerId { get; set; }
    public Manufacturer? Manufacturer { get; set; }

    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    public ICollection<ProductBatch> Batches { get; set; } = new List<ProductBatch>();
}
