namespace PharmaERP.Application.DTOs;

public record ProductDto(
    int Id,
    string Name,
    string? GenericName,
    string? ProductCode,
    string? Barcode,
    decimal DefaultPurchasePrice,
    decimal DefaultSalePrice,
    bool IsActive,
    int? CategoryId,
    string? CategoryName,
    int? ManufacturerId,
    string? ManufacturerName,
    int? UnitId,
    string? UnitAbbreviation,
    byte[] RowVersion);

public class ProductUpsertDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? ProductCode { get; set; }
    public string? Barcode { get; set; }
    public decimal DefaultPurchasePrice { get; set; }
    public decimal DefaultSalePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CategoryId { get; set; }
    public int? ManufacturerId { get; set; }
    public int? UnitId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

