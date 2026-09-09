namespace PharmaERP.Application.DTOs;

public record CustomerDto(
    int Id,
    string CustomerCode,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Address,
    decimal CreditLimit,
    bool IsActive,
    byte[] RowVersion);

public class CustomerUpsertDto
{
    public int Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

