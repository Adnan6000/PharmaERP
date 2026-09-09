using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.DTOs;

public record ProductBatchDto(
    int Id,
    int ProductId,
    string ProductName,
    string BatchNumber,
    string NormalizedBatchNumber,
    DateOnly? ManufacturingDate,
    DateOnly ExpiryDate,
    decimal QuantityOnHand,
    decimal InventoryValue,
    decimal AveragePurchaseCost,
    decimal LastPurchaseCost,
    decimal? SuggestedSalePrice,
    bool IsActive,
    bool IsExpired,
    bool IsNearExpiry,
    int DaysUntilExpiry,
    byte[] RowVersion);

public class OpeningStockCreateDto
{
    public int ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SuggestedSalePrice { get; set; }
}

public record CurrentStockDto(
    int ProductId,
    string ProductName,
    string? ProductCode,
    string? ManufacturerName,
    string? CategoryName,
    decimal TotalQuantity,
    decimal EstimatedInventoryValue);

public record BatchStockDto(
    int BatchId,
    int ProductId,
    string ProductName,
    string BatchNumber,
    DateOnly ExpiryDate,
    decimal QuantityOnHand,
    decimal AveragePurchaseCost,
    decimal InventoryValue,
    string Status);

public record StockMovementDto(
    int Id,
    int ProductId,
    string ProductName,
    int ProductBatchId,
    string BatchNumber,
    StockMovementType MovementType,
    decimal QuantityDelta,
    decimal BalanceAfter,
    decimal UnitCost,
    decimal InventoryCostRate,
    decimal InventoryValueDelta,
    decimal InventoryValueAfter,
    StockReferenceDocumentType ReferenceDocumentType,
    int? ReferenceDocumentId,
    int? ReferenceDocumentItemId,
    string? ReferenceDocumentNumber,
    int? ReversesStockMovementId,
    DateTime TransactionDateUtc,
    string? Remarks);

public record ExpiryReportItemDto(
    int ProductId,
    string ProductName,
    int BatchId,
    string BatchNumber,
    DateOnly ExpiryDate,
    decimal QuantityOnHand,
    decimal AveragePurchaseCost,
    decimal InventoryValue,
    int DaysUntilExpiry,
    string Status);

