using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.DTOs;

public class SaleReturnCreateDto
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public int OriginalSaleInvoiceId { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public string? Reason { get; set; }
    public List<SaleReturnItemCreateDto> Items { get; set; } = new();
}

public class SaleReturnItemCreateDto
{
    public int OriginalSaleInvoiceItemId { get; set; }
    public decimal Quantity { get; set; }
}

public class SaleReturnDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    public int OriginalSaleInvoiceId { get; set; }
    public string OriginalSaleInvoiceNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public DateOnly ReturnDate { get; set; }
    public string? Reason { get; set; }
    public decimal TotalAmount { get; set; }
    public SaleReturnStatus Status { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public List<SaleReturnItemDto> Items { get; set; } = new();
}

public class SaleReturnItemDto
{
    public int Id { get; set; }
    public int SaleReturnId { get; set; }
    public int OriginalSaleInvoiceItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal RefundAmount { get; set; }

    public List<SaleReturnBatchAllocationDto> BatchAllocations { get; set; } = new();
}

public class SaleReturnBatchAllocationDto
{
    public int Id { get; set; }
    public int SaleReturnItemId { get; set; }
    public int OriginalSaleInvoiceBatchAllocationId { get; set; }
    public int ProductBatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal RestoredInventoryCostRate { get; set; }
    public decimal RestoredInventoryValue { get; set; }
}

