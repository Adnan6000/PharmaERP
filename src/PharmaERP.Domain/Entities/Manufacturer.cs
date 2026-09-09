using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

public class Manufacturer : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = [];
}

