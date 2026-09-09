using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public class ProductLookupService : IProductLookupService
{
    private readonly IProductRepository _productRepository;
    private readonly IProductBatchRepository _batchRepository;
    private readonly IBusinessClock _businessClock;

    public ProductLookupService(
        IProductRepository productRepository,
        IProductBatchRepository batchRepository,
        IBusinessClock businessClock)
    {
        _productRepository = productRepository;
        _batchRepository = batchRepository;
        _businessClock = businessClock;
    }

    public async Task<ProductLookupResultDto> LookupByBarcodeOrCodeAsync(
        string codeOrBarcode,
        DateOnly? businessDate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeOrBarcode))
        {
            return new ProductLookupResultDto
            {
                Status = ProductLookupStatus.NotFound,
                Message = "Please enter a product code or scan a barcode."
            };
        }

        string term = codeOrBarcode.Trim();
        var effectiveDate = businessDate ?? await _businessClock.GetBusinessDateAsync(cancellationToken);

        // First attempt barcode lookup
        var product = await _productRepository.GetByBarcodeAsync(term, cancellationToken);
        if (product == null)
        {
            // Attempt code lookup
            product = await _productRepository.GetByProductCodeAsync(term, cancellationToken);
        }

        if (product == null)
        {
            return new ProductLookupResultDto
            {
                Status = ProductLookupStatus.NotFound,
                Message = $"Product with barcode or code '{term}' was not found."
            };
        }

        return await EvaluateProductStockStatusAsync(product, effectiveDate, cancellationToken);
    }

    public async Task<List<ProductLookupResultDto>> SearchProductsAsync(
        string query,
        DateOnly? businessDate = null,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<ProductLookupResultDto>();
        }

        var effectiveDate = businessDate ?? await _businessClock.GetBusinessDateAsync(cancellationToken);
        var products = await _productRepository.SearchProductsAsync(query.Trim(), maxResults, cancellationToken);

        var results = new List<ProductLookupResultDto>();
        foreach (var p in products)
        {
            var res = await EvaluateProductStockStatusAsync(p, effectiveDate, cancellationToken);
            results.Add(res);
        }

        return results;
    }

    private async Task<ProductLookupResultDto> EvaluateProductStockStatusAsync(
        Product product,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var productDto = new ProductDto(
            product.Id,
            product.Name,
            product.GenericName,
            product.ProductCode,
            product.Barcode,
            product.DefaultPurchasePrice,
            product.DefaultSalePrice,
            product.IsActive,
            product.CategoryId,
            product.Category?.Name,
            product.ManufacturerId,
            product.Manufacturer?.Name,
            product.UnitId,
            product.Unit?.Name,
            product.RowVersion);

        if (!product.IsActive)
        {
            return new ProductLookupResultDto
            {
                Status = ProductLookupStatus.Inactive,
                Product = productDto,
                Message = $"Product '{product.Name}' ({product.ProductCode}) is inactive and cannot be sold."
            };
        }

        var batches = await _batchRepository.GetActiveFefoBatchesAsync(product.Id, cancellationToken);

        decimal totalPhysicalQty = batches.Sum(b => b.QuantityOnHand);
        if (totalPhysicalQty <= 0)
        {
            return new ProductLookupResultDto
            {
                Status = ProductLookupStatus.OutOfStock,
                Product = productDto,
                TotalPhysicalQuantity = 0,
                TotalSaleableQuantity = 0,
                DefaultSalePrice = product.DefaultSalePrice,
                Message = $"Product '{product.Name}' ({product.ProductCode}) is out of stock (0 units on hand)."
            };
        }

        var unexpiredBatches = batches
            .Where(b => b.ExpiryDate > effectiveDate && b.QuantityOnHand > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Id)
            .ToList();

        decimal totalSaleableQty = unexpiredBatches.Sum(b => b.QuantityOnHand);

        if (totalSaleableQty <= 0)
        {
            return new ProductLookupResultDto
            {
                Status = ProductLookupStatus.ExpiredOnly,
                Product = productDto,
                TotalPhysicalQuantity = totalPhysicalQty,
                TotalSaleableQuantity = 0,
                DefaultSalePrice = product.DefaultSalePrice,
                Message = $"Product '{product.Name}' ({product.ProductCode}) only has expired stock ({totalPhysicalQty} units expired) and cannot be sold."
            };
        }

        var batchDtos = unexpiredBatches.Select(b => new ProductBatchLookupDto
        {
            BatchId = b.Id,
            BatchNumber = b.BatchNumber,
            ExpiryDate = b.ExpiryDate,
            QuantityOnHand = b.QuantityOnHand,
            SuggestedSalePrice = b.SuggestedSalePrice,
            IsExpired = b.ExpiryDate <= effectiveDate
        }).ToList();

        decimal defaultPrice = unexpiredBatches.First().SuggestedSalePrice ?? product.DefaultSalePrice;

        return new ProductLookupResultDto
        {
            Status = ProductLookupStatus.Available,
            Product = productDto,
            TotalPhysicalQuantity = totalPhysicalQty,
            TotalSaleableQuantity = totalSaleableQty,
            EarliestExpiryDate = unexpiredBatches.First().ExpiryDate,
            DefaultSalePrice = defaultPrice,
            EligibleBatches = batchDtos,
            Message = $"Available: {totalSaleableQty} units across {unexpiredBatches.Count} batch(es)."
        };
    }
}

