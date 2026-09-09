namespace PharmaERP.Application.DTOs;

public class BusinessProfileDto
{
    public string PharmacyName { get; set; } = "PharmaCare Pharmacy";
    public string Address { get; set; } = "Main Commercial Area, City Center";
    public string Phone { get; set; } = "+92 300 1234567";
    public string DrugLicenseNumber { get; set; } = "DL-1234-PB";
    public string TaxNumber { get; set; } = "NTN-9876543-1";
    public string ReceiptFooter { get; set; } = "Thank you for your visit. Medicines once sold cannot be returned without receipt.";
    public string TimeZoneId { get; set; } = "Pakistan Standard Time";
}

