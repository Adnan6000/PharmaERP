using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.DTOs;

public record PurchaseInvoiceItemDto(
    int Id,
    int PurchaseInvoiceId,
    int ProductId,
    string ProductName,
    int ProductBatchId,
    string BatchNumber,
    DateOnly ExpiryDate,
    DateOnly? ManufacturingDate,
    decimal Quantity,
    decimal PurchaseRate,
    decimal? SuggestedSaleRate,
    decimal DiscountAmount,
    decimal LineTotal,
    decimal ReturnedQuantity,
    decimal RemainingReturnableQuantity);

public record PurchaseInvoiceDto(
    int Id,
    string InvoiceNumber,
    string? SupplierInvoiceNumber,
    int SupplierId,
    string SupplierName,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    string? Remarks,
    decimal GrossTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetTotal,
    PurchaseInvoiceStatus Status,
    DateTime? PostedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<PurchaseInvoiceItemDto> Items,
    byte[] RowVersion);

public record PurchaseInvoiceListDto(
    int Id,
    string InvoiceNumber,
    string? SupplierInvoiceNumber,
    int SupplierId,
    string SupplierName,
    DateOnly InvoiceDate,
    decimal NetTotal,
    PurchaseInvoiceStatus Status,
    int ItemCount,
    DateTime? PostedAtUtc);

public class PurchaseInvoiceItemCreateDto
{
    public int ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal? SuggestedSaleRate { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class PurchaseInvoiceCreateDto
{
    public int SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Remarks { get; set; }
    public List<PurchaseInvoiceItemCreateDto> Items { get; set; } = [];
}

public record PurchaseReturnItemDto(
    int Id,
    int PurchaseReturnId,
    int? OriginalPurchaseInvoiceItemId,
    int ProductId,
    string ProductName,
    int ProductBatchId,
    string BatchNumber,
    decimal Quantity,
    decimal ReturnRate,
    decimal LineTotal);

public record PurchaseReturnDto(
    int Id,
    string ReturnNumber,
    int? OriginalPurchaseInvoiceId,
    string? OriginalPurchaseInvoiceNumber,
    int SupplierId,
    string SupplierName,
    DateOnly ReturnDate,
    string? Reason,
    decimal TotalAmount,
    PurchaseReturnStatus Status,
    DateTime? PostedAtUtc,
    IReadOnlyList<PurchaseReturnItemDto> Items,
    byte[] RowVersion);

public record PurchaseReturnListDto(
    int Id,
    string ReturnNumber,
    string? OriginalPurchaseInvoiceNumber,
    int SupplierId,
    string SupplierName,
    DateOnly ReturnDate,
    decimal TotalAmount,
    PurchaseReturnStatus Status,
    int ItemCount,
    DateTime? PostedAtUtc);

public class PurchaseReturnItemCreateDto
{
    public int? OriginalPurchaseInvoiceItemId { get; set; }
    public int ProductId { get; set; }
    public int ProductBatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? ReturnRate { get; set; } // Required only for unlinked return
}

public class PurchaseReturnCreateDto
{
    public int SupplierId { get; set; }
    public int? OriginalPurchaseInvoiceId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public string? Reason { get; set; }
    public List<PurchaseReturnItemCreateDto> Items { get; set; } = [];
}

