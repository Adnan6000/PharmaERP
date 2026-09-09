using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.DTOs;

public class InvoicePrintDataDto
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDateTimeUtc { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public SaleType SaleType { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }

    public decimal GrossTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal InvoiceDiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetTotal { get; set; }
    public decimal? TenderedAmount { get; set; }
    public decimal? ChangeGiven { get; set; }

    // Print Identity Snapshots
    public string PharmacyName { get; set; } = string.Empty;
    public string PharmacyAddress { get; set; } = string.Empty;
    public string PharmacyPhone { get; set; } = string.Empty;
    public string DrugLicenseNo { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string ReceiptFooter { get; set; } = string.Empty;

    public List<InvoicePrintItemDto> Items { get; set; } = new();
}

public class InvoicePrintItemDto
{
    public int LineNumber { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal InvoiceDiscountAllocated { get; set; }
    public decimal NetLineAmount { get; set; }

    public List<InvoicePrintAllocationDto> BatchAllocations { get; set; } = new();
}

public class InvoicePrintAllocationDto
{
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
}

