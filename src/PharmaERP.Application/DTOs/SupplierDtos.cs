namespace PharmaERP.Application.DTOs;

public record SupplierDto(
    int Id,
    string SupplierCode,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Address,
    bool IsActive,
    byte[] RowVersion);

public class SupplierUpsertDto
{
    public int Id { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

