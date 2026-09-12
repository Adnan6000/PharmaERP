using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Tests;

public class SaleServiceTests
{
    private class FakeSaleTransactionWriter : ISaleTransactionWriter
    {
        public Task<SaleInvoiceDto> CreateAndPostSaleInvoiceAsync(string invoiceNumber, SaleInvoiceCreateDto dto, CancellationToken cancellationToken = default)
        {
            var invoiceDto = new SaleInvoiceDto
            {
                Id = 100,
                InvoiceNumber = invoiceNumber,
                SaleType = dto.SaleType,
                CustomerId = dto.CustomerId,
                CustomerNameSnapshot = "Test Customer",
                GrossTotal = 100m,
                LineDiscountTotal = 0m,
                InvoiceDiscountAmount = dto.InvoiceDiscountAmount,
                TaxAmount = 0m,
                NetTotal = 100m - dto.InvoiceDiscountAmount,
                TenderedAmount = dto.TenderedAmount,
                ChangeGiven = dto.TenderedAmount.HasValue ? dto.TenderedAmount.Value - (100m - dto.InvoiceDiscountAmount) : 0m,
                Status = SaleInvoiceStatus.Posted,
                OperationId = dto.OperationId,
                Items = dto.Items.Select(i => new SaleInvoiceItemDto
                {
                    Id = 1,
                    SaleInvoiceId = 100,
                    ProductId = i.ProductId,
                    ProductNameSnapshot = "Product " + i.ProductId,
                    Quantity = i.Quantity,
                    UnitSalePrice = i.UnitSalePrice,
                    LineDiscountAmount = i.LineDiscountAmount,
                    GrossAmount = i.Quantity * i.UnitSalePrice,
                    NetLineAmount = i.Quantity * i.UnitSalePrice - i.LineDiscountAmount
                }).ToList()
            };
            return Task.FromResult(invoiceDto);
        }

        public Task<SaleReturnDto> CreateAndPostSaleReturnAsync(string returnNumber, SaleReturnCreateDto dto, CancellationToken cancellationToken = default)
        {
            var returnDto = new SaleReturnDto
            {
                Id = 200,
                ReturnNumber = returnNumber,
                OriginalSaleInvoiceId = dto.OriginalSaleInvoiceId,
                OriginalSaleInvoiceNumber = "SINV-202609-000001",
                TotalAmount = 50m,
                Status = SaleReturnStatus.Posted,
                OperationId = dto.OperationId
            };
            return Task.FromResult(returnDto);
        }

        public Task<SaleInvoiceDto> CancelSaleInvoiceAsync(int saleInvoiceId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaleInvoiceDto { Id = saleInvoiceId, Status = SaleInvoiceStatus.Cancelled });

        public Task<SaleReturnDto> CancelSaleReturnAsync(int saleReturnId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaleReturnDto { Id = saleReturnId, Status = SaleReturnStatus.Cancelled });
    }

    private class FakeSaleInvoiceRepo : ISaleInvoiceRepository
    {
        public Task<SaleInvoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleInvoice?>(null);

        public Task<SaleInvoice?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleInvoice?>(null);

        public Task<SaleInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleInvoice?>(null);

        public Task<List<SaleInvoiceDto>> GetInvoicesAsync(
            DateOnly? fromDate,
            DateOnly? toDate,
            int? customerId,
            SaleType? saleType,
            SaleInvoiceStatus? status,
            string? searchTerm,
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<SaleInvoiceDto>());
    }

    private class FakeProductBatchRepo : IProductBatchRepository
    {
        public Task<ProductBatch?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductBatch?>(null);

        public Task<ProductBatch?> GetByNormalizedAsync(int productId, string normalizedBatchNumber, DateOnly expiryDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductBatch?>(null);

        public Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductBatchDto>>(new List<ProductBatchDto>
            {
                new(
                    1,
                    productId,
                    "Product " + productId,
                    "BATCH-A",
                    "BATCH-A",
                    null,
                    DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
                    10m,
                    500m,
                    50m,
                    50m,
                    75m,
                    true,
                    false,
                    false,
                    180,
                    Array.Empty<byte>())
            });

        public Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<BatchStockDto>([], 0, 1, 20));

        public Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<CurrentStockDto>([], 0, 1, 20));

        public Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ExpiryReportItemDto>([], 0, 1, 20));

        public Task<decimal> GetTotalInventoryValuationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);
    }

    private class FakeProductRepo : IProductRepository
    {
        public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(new Product
            {
                Id = id,
                ProductCode = "PROD-1",
                Name = "Test Med",
                GenericName = "Paracetamol",
                DefaultPurchasePrice = 40m,
                DefaultSalePrice = 60m,
                IsActive = true
            });

        public Task<PagedResult<ProductDto>> GetPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ProductDto>([], 0, 1, 20));

        public Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default) => Task.FromResult(product);
        public Task UpdateAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsBarcodeAsync(string barcode, int? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(null);
        public Task<Product?> GetByProductCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(null);
        public Task<List<Product>> SearchProductsAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default) => Task.FromResult(new List<Product>());
    }

    private class FakeDocNumberGen : IDocumentNumberGenerator
    {
        public Task<string> NextDocumentNumberAsync(DocumentType type, int? fiscalYear = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("SINV-202609-000001");
    }

    private class FakeBusinessClock : IBusinessClock
    {
        public Task<DateTime> GetServerUtcNowAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DateTime.UtcNow);
        public Task<DateOnly> GetBusinessDateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DateOnly.FromDateTime(DateTime.Today));
        public Task<DateTime> ToBusinessTimeAsync(DateTime utcDateTime, CancellationToken cancellationToken = default) =>
            Task.FromResult(utcDateTime.AddHours(5));
        public Task<DateOnly> ToBusinessDateAsync(DateTime utcDateTime, CancellationToken cancellationToken = default) =>
            Task.FromResult(DateOnly.FromDateTime(utcDateTime.AddHours(5)));
    }

    [Fact]
    public async Task PreviewSaleAsync_CalculatesTotalsAndAllocatesFEFOPreview()
    {
        var service = new SaleService(
            new FakeSaleTransactionWriter(),
            new FakeSaleInvoiceRepo(),
            new FakeDocNumberGen(),
            new FakeProductBatchRepo(),
            new FakeProductRepo(),
            new FakeBusinessClock());

        var dto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 100m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = 1,
                    Quantity = 5m,
                    UnitSalePrice = 20m,
                    LineDiscountAmount = 2m
                }
            },
            InvoiceDiscountAmount = 5m
        };

        var preview = await service.PreviewSaleAsync(dto);

        Assert.Equal(100m, preview.GrossTotal);
        Assert.Equal(2m, preview.LineDiscountTotal);
        Assert.Equal(5m, preview.InvoiceDiscountAmount);
        Assert.Equal(93m, preview.NetTotal);
        Assert.Single(preview.Items);
        Assert.Single(preview.Items[0].Allocations);
        Assert.Equal(5m, preview.Items[0].Allocations[0].Quantity);
    }

    [Fact]
    public async Task PreviewSaleAsync_ThrowsValidationException_WhenItemsEmpty()
    {
        var service = new SaleService(
            new FakeSaleTransactionWriter(),
            new FakeSaleInvoiceRepo(),
            new FakeDocNumberGen(),
            new FakeProductBatchRepo(),
            new FakeProductRepo(),
            new FakeBusinessClock());

        var dto = new SaleInvoiceCreateDto
        {
            Items = new List<SaleInvoiceItemCreateDto>()
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewSaleAsync(dto));
    }

    [Fact]
    public async Task PostSaleAsync_ValidatesCreditCustomerRequirements()
    {
        var service = new SaleService(
            new FakeSaleTransactionWriter(),
            new FakeSaleInvoiceRepo(),
            new FakeDocNumberGen(),
            new FakeProductBatchRepo(),
            new FakeProductRepo(),
            new FakeBusinessClock());

        var dto = new SaleInvoiceCreateDto
        {
            SaleType = SaleType.Credit,
            CustomerId = null,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = 1, Quantity = 1, UnitSalePrice = 10 }
            }
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.PostSaleAsync(dto));
    }
}
