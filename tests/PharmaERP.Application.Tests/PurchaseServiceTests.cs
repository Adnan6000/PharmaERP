using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Tests;

public class PurchaseServiceTests
{
    private class FakePurchaseInvoiceRepo : IPurchaseInvoiceRepository
    {
        public readonly List<PurchaseInvoice> Invoices = [];

        public Task<PagedResult<PurchaseInvoiceListDto>> GetPagedAsync(PurchaseInvoiceFilterQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<PurchaseInvoiceListDto>([], 0, 1, 20));

        public Task<PurchaseInvoiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseInvoiceDto?>(null);

        public Task<bool> ExistsSupplierInvoiceNumberAsync(int supplierId, string normalizedNumber, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            bool exists = Invoices.Any(i => i.SupplierId == supplierId &&
                                           i.NormalizedSupplierInvoiceNumber == normalizedNumber &&
                                           (!excludeId.HasValue || i.Id != excludeId.Value));
            return Task.FromResult(exists);
        }
    }

    private class FakePurchaseReturnRepo : IPurchaseReturnRepository
    {
        public Task<PagedResult<PurchaseReturnListDto>> GetPagedAsync(PurchaseReturnFilterQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<PurchaseReturnListDto>([], 0, 1, 20));

        public Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseReturnDto?>(null);
    }

    private class FakePurchaseWriter : IPurchaseTransactionWriter
    {
        public Task<PurchaseInvoiceDto> CreateAndPostPurchaseInvoiceAsync(string invoiceNumber, PurchaseInvoiceCreateDto dto, CancellationToken cancellationToken = default)
        {
            decimal gross = dto.Items.Sum(i => i.Quantity * i.PurchaseRate);
            decimal discount = dto.Items.Sum(i => i.DiscountAmount);
            return Task.FromResult(new PurchaseInvoiceDto(
                1,
                invoiceNumber,
                dto.SupplierInvoiceNumber,
                dto.SupplierId,
                "Vendor",
                dto.InvoiceDate,
                dto.DueDate,
                dto.Remarks,
                gross,
                discount,
                0m,
                gross - discount,
                PurchaseInvoiceStatus.Posted,
                DateTime.UtcNow,
                null,
                null,
                [],
                []));
        }

        public Task CancelPurchaseInvoiceAsync(int purchaseInvoiceId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CancelPurchaseReturnAsync(int purchaseReturnId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PurchaseReturnDto> CreateAndPostPurchaseReturnAsync(string returnNumber, PurchaseReturnCreateDto dto, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PurchaseReturnDto(
                1,
                returnNumber,
                dto.OriginalPurchaseInvoiceId,
                null,
                dto.SupplierId,
                "Vendor",
                dto.ReturnDate,
                dto.Reason,
                dto.Items.Sum(i => i.Quantity * (i.ReturnRate ?? 0m)),
                PurchaseReturnStatus.Posted,
                DateTime.UtcNow,
                [],
                []));
        }
    }

    private class FakeDocNumberGen : IDocumentNumberGenerator
    {
        public Task<string> NextDocumentNumberAsync(DocumentType type, int? year = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"PINV-{year ?? 2026}-000001");
    }

    [Fact]
    public async Task CreateAndPostPurchaseInvoiceAsync_Throws_WhenDuplicateLineBatches()
    {
        var service = new PurchaseService(new FakePurchaseInvoiceRepo(), new FakePurchaseReturnRepo(), new FakePurchaseWriter(), new FakeDocNumberGen());

        var exp = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
        var dto = new PurchaseInvoiceCreateDto
        {
            SupplierId = 1,
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto { ProductId = 1, BatchNumber = "B100", ExpiryDate = exp, Quantity = 5, PurchaseRate = 10 },
                new PurchaseInvoiceItemCreateDto { ProductId = 1, BatchNumber = "B100", ExpiryDate = exp, Quantity = 10, PurchaseRate = 10 }
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAndPostPurchaseInvoiceAsync(dto));
    }

    [Fact]
    public async Task CreateAndPostPurchaseInvoiceAsync_ThrowsDuplicate_WhenSupplierBillNumberExists()
    {
        var invoiceRepo = new FakePurchaseInvoiceRepo();
        invoiceRepo.Invoices.Add(new PurchaseInvoice
        {
            Id = 1,
            SupplierId = 1,
            SupplierInvoiceNumber = "BILL-999",
            NormalizedSupplierInvoiceNumber = "BILL-999"
        });

        var service = new PurchaseService(invoiceRepo, new FakePurchaseReturnRepo(), new FakePurchaseWriter(), new FakeDocNumberGen());

        var dto = new PurchaseInvoiceCreateDto
        {
            SupplierId = 1,
            SupplierInvoiceNumber = "BILL-999",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto { ProductId = 1, BatchNumber = "B200", ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)), Quantity = 5, PurchaseRate = 10 }
            ]
        };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => service.CreateAndPostPurchaseInvoiceAsync(dto));
    }

    [Fact]
    public async Task CreateAndPostPurchaseInvoiceAsync_PostsSuccessfully_WhenValid()
    {
        var service = new PurchaseService(new FakePurchaseInvoiceRepo(), new FakePurchaseReturnRepo(), new FakePurchaseWriter(), new FakeDocNumberGen());

        var dto = new PurchaseInvoiceCreateDto
        {
            SupplierId = 1,
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items =
            [
                new PurchaseInvoiceItemCreateDto
                {
                    ProductId = 1,
                    BatchNumber = "B300",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 20,
                    PurchaseRate = 15,
                    DiscountAmount = 10
                }
            ]
        };

        var result = await service.CreateAndPostPurchaseInvoiceAsync(dto);

        Assert.Equal("PINV-2026-000001", result.InvoiceNumber);
        Assert.Equal(300m, result.GrossTotal);
        Assert.Equal(10m, result.DiscountAmount);
        Assert.Equal(290m, result.NetTotal);
        Assert.Equal(PurchaseInvoiceStatus.Posted, result.Status);
    }
}

