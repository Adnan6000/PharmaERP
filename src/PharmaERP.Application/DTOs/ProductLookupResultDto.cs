namespace PharmaERP.Application.DTOs;

public enum ProductLookupStatus
{
    Available = 1,
    NotFound = 2,
    Inactive = 3,
    OutOfStock = 4,
    ExpiredOnly = 5
}

public class ProductLookupResultDto
{
    public ProductLookupStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public ProductDto? Product { get; set; }
    public decimal TotalSaleableQuantity { get; set; }
    public decimal TotalPhysicalQuantity { get; set; }
    public DateOnly? EarliestExpiryDate { get; set; }
    public decimal DefaultSalePrice { get; set; }
    public List<ProductBatchLookupDto> EligibleBatches { get; set; } = new();
}

public class ProductBatchLookupDto
{
    public int BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal? SuggestedSalePrice { get; set; }
    public bool IsExpired { get; set; }
}

