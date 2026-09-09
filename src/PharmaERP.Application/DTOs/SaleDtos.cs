using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.DTOs;

public class SaleInvoiceCreateDto
{
    public Guid OperationId { get; set; } = Guid.NewGuid();

    public DateOnly? BusinessDate { get; set; }

    public int? CustomerId { get; set; }

    public SaleType SaleType { get; set; } = SaleType.Cash;

    public string? Reference { get; set; }

    public string? Remarks { get; set; }

    public decimal InvoiceDiscountAmount { get; set; }

    public decimal? TenderedAmount { get; set; }

    public List<SaleInvoiceItemCreateDto> Items { get; set; } = new();
}

public class SaleInvoiceItemCreateDto
{
    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitSalePrice { get; set; }

    public decimal LineDiscountAmount { get; set; }

    public int? ExplicitBatchId { get; set; }
}

public class SaleInvoiceDto
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    public DateTime InvoiceDateTimeUtc { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
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
    public SaleInvoiceStatus Status { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    // Print Identity Snapshots
    public string? PharmacyNameSnapshot { get; set; }
    public string? PharmacyAddressSnapshot { get; set; }
    public string? PharmacyPhoneSnapshot { get; set; }
    public string? DrugLicenseNoSnapshot { get; set; }
    public string? TaxNumberSnapshot { get; set; }
    public string? ReceiptFooterSnapshot { get; set; }

    public List<SaleInvoiceItemDto> Items { get; set; } = new();
}

public class SaleInvoiceItemDto
{
    public int Id { get; set; }
    public int SaleInvoiceId { get; set; }
    public int ProductId { get; set; }
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal InvoiceDiscountAllocated { get; set; }
    public decimal TaxAllocated { get; set; }
    public decimal NetLineAmount { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal RefundedAmount { get; set; }

    public List<SaleInvoiceBatchAllocationDto> BatchAllocations { get; set; } = new();
}

public class SaleInvoiceBatchAllocationDto
{
    public int Id { get; set; }
    public int SaleInvoiceItemId { get; set; }
    public int ProductBatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal InventoryCostRate { get; set; }
    public decimal InventoryValueConsumed { get; set; }
    public string BatchNumberSnapshot { get; set; } = string.Empty;
    public DateOnly ExpiryDateSnapshot { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal ReturnedInventoryValue { get; set; }
}

public class SalePreviewDto
{
    public decimal GrossTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal InvoiceDiscountAmount { get; set; }
    public decimal NetTotal { get; set; }
    public List<SalePreviewItemDto> Items { get; set; } = new();
}

public class SalePreviewItemDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal InvoiceDiscountAllocated { get; set; }
    public decimal NetLineAmount { get; set; }
    public List<SalePreviewAllocationDto> Allocations { get; set; } = new();
}

public class SalePreviewAllocationDto
{
    public int ProductBatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
}

