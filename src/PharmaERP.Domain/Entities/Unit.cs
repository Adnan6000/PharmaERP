using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Represents a basic unit of measurement or packaging (e.g., Box, Strip, Tablet, Bottle).
/// Prepared for future multi-tier packaging conversion hierarchies.
/// </summary>
public class Unit : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = [];
}

