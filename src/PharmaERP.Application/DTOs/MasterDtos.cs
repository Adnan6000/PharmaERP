namespace PharmaERP.Application.DTOs;

public record ManufacturerDto(
    int Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    bool IsActive,
    byte[] RowVersion);

public class ManufacturerUpsertDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public record CategoryDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    byte[] RowVersion);

public class CategoryUpsertDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public record UnitDto(
    int Id,
    string Name,
    string Abbreviation,
    bool IsActive,
    byte[] RowVersion);

public class UnitUpsertDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

