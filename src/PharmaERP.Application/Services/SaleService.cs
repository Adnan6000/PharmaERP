using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class SaleService : ISaleService
{
    private readonly ISaleTransactionWriter _transactionWriter;
    private readonly ISaleInvoiceRepository _invoiceRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly IProductBatchRepository _batchRepository;
    private readonly IProductRepository _productRepository;
    private readonly IBusinessClock _businessClock;

    public SaleService(
        ISaleTransactionWriter transactionWriter,
        ISaleInvoiceRepository invoiceRepository,
        IDocumentNumberGenerator documentNumberGenerator,
        IProductBatchRepository batchRepository,
        IProductRepository productRepository,
        IBusinessClock businessClock)
    {
        _transactionWriter = transactionWriter;
        _invoiceRepository = invoiceRepository;
        _documentNumberGenerator = documentNumberGenerator;
        _batchRepository = batchRepository;
        _productRepository = productRepository;
        _businessClock = businessClock;
    }

    public async Task<SalePreviewDto> PreviewSaleAsync(SaleInvoiceCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ValidationException("Sale preview requires at least one item.");
        }

        var preview = new SalePreviewDto();
        decimal totalGross = 0m;
        decimal totalLineDiscount = 0m;

        var itemsList = new List<SalePreviewItemDto>();

        foreach (var item in dto.Items)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException($"Product with ID {item.ProductId} was not found.");

            decimal gross = Math.Round(item.Quantity * item.UnitSalePrice, 4);
            decimal lineDiscount = Math.Round(item.LineDiscountAmount, 4);
            totalGross += gross;
            totalLineDiscount += lineDiscount;

            var previewItem = new SalePreviewItemDto
            {
                ProductId = item.ProductId,
                ProductCode = product.ProductCode ?? string.Empty,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitSalePrice = item.UnitSalePrice,
                GrossAmount = gross,
                LineDiscountAmount = lineDiscount
            };

            // Query unexpired batches for preview
            var effectiveDate = dto.BusinessDate ?? await _businessClock.GetBusinessDateAsync(cancellationToken);
            var batches = await _batchRepository.GetActiveFefoBatchesAsync(item.ProductId, cancellationToken);
            var unexpired = batches.Where(b => b.ExpiryDate > effectiveDate && b.QuantityOnHand > 0)
                                   .OrderBy(b => b.ExpiryDate)
                                   .ThenBy(b => b.Id)
                                   .ToList();

            decimal remainingQty = item.Quantity;
            foreach (var b in unexpired)
            {
                if (remainingQty <= 0) break;
                decimal take = Math.Min(remainingQty, b.QuantityOnHand);
                previewItem.Allocations.Add(new SalePreviewAllocationDto
                {
                    ProductBatchId = b.Id,
                    BatchNumber = b.BatchNumber,
                    ExpiryDate = b.ExpiryDate,
                    Quantity = take
                });
                remainingQty -= take;
            }

            itemsList.Add(previewItem);
        }

        // Prorate invoice discount
        decimal invoiceDiscount = Math.Round(dto.InvoiceDiscountAmount, 4);
        decimal totalBase = totalGross - totalLineDiscount;

        if (totalBase > 0 && invoiceDiscount > 0)
        {
            decimal allocatedSum = 0m;
            for (int i = 0; i < itemsList.Count; i++)
            {
                var itm = itemsList[i];
                decimal itemBase = itm.GrossAmount - itm.LineDiscountAmount;
                if (i == itemsList.Count - 1)
                {
                    // Last item absorbs rounding difference
                    itm.InvoiceDiscountAllocated = Math.Round(invoiceDiscount - allocatedSum, 4);
                }
                else
                {
                    decimal share = Math.Round(invoiceDiscount * (itemBase / totalBase), 4);
                    itm.InvoiceDiscountAllocated = share;
                    allocatedSum += share;
                }
                itm.NetLineAmount = itm.GrossAmount - itm.LineDiscountAmount - itm.InvoiceDiscountAllocated;
            }
        }
        else
        {
            foreach (var itm in itemsList)
            {
                itm.InvoiceDiscountAllocated = 0m;
                itm.NetLineAmount = itm.GrossAmount - itm.LineDiscountAmount;
            }
        }

        preview.GrossTotal = totalGross;
        preview.LineDiscountTotal = totalLineDiscount;
        preview.InvoiceDiscountAmount = invoiceDiscount;
        preview.NetTotal = totalGross - totalLineDiscount - invoiceDiscount;
        preview.Items = itemsList;

        return preview;
    }

    public async Task<SaleInvoiceDto> PostSaleAsync(SaleInvoiceCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ValidationException("Sale invoice must contain at least one item.");
        }

        if (dto.SaleType == SaleType.Credit && (!dto.CustomerId.HasValue || dto.CustomerId <= 0))
        {
            throw new ValidationException("Credit sales require a valid registered customer.");
        }

        // Check if invoice for this OperationId was already committed (Idempotency Recovery - Requirement 11)
        var existing = await _invoiceRepository.GetByOperationIdAsync(dto.OperationId, cancellationToken);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        string invoiceNumber = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.SaleInvoice, cancellationToken: cancellationToken);
        return await _transactionWriter.CreateAndPostSaleInvoiceAsync(invoiceNumber, dto, cancellationToken);
    }

    public async Task<SaleInvoiceDto> CancelSaleAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
        {
            throw new ValidationException("Cancellation reason is required.");
        }

        return await _transactionWriter.CancelSaleInvoiceAsync(id, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<SaleInvoiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(id, cancellationToken);
        return invoice == null ? null : MapToDto(invoice);
    }

    public async Task<List<SaleInvoiceDto>> SearchInvoicesAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleType? saleType,
        SaleInvoiceStatus? status,
        string? searchTerm,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _invoiceRepository.GetInvoicesAsync(fromDate, toDate, customerId, saleType, status, searchTerm, take, cancellationToken);
    }

    private static SaleInvoiceDto MapToDto(Domain.Entities.SaleInvoice invoice)
    {
        return new SaleInvoiceDto
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            OperationId = invoice.OperationId,
            InvoiceDateTimeUtc = invoice.InvoiceDateTimeUtc,
            BusinessDate = invoice.BusinessDate,
            CustomerId = invoice.CustomerId,
            CustomerNameSnapshot = invoice.CustomerNameSnapshot,
            SaleType = invoice.SaleType,
            Reference = invoice.Reference,
            Remarks = invoice.Remarks,
            GrossTotal = invoice.GrossTotal,
            LineDiscountTotal = invoice.LineDiscountTotal,
            InvoiceDiscountAmount = invoice.InvoiceDiscountAmount,
            TaxAmount = invoice.TaxAmount,
            NetTotal = invoice.NetTotal,
            TenderedAmount = invoice.TenderedAmount,
            ChangeGiven = invoice.ChangeGiven,
            Status = invoice.Status,
            PostedAtUtc = invoice.PostedAtUtc,
            CancelledAtUtc = invoice.CancelledAtUtc,
            CancellationReason = invoice.CancellationReason,
            PharmacyNameSnapshot = invoice.PharmacyNameSnapshot,
            PharmacyAddressSnapshot = invoice.PharmacyAddressSnapshot,
            PharmacyPhoneSnapshot = invoice.PharmacyPhoneSnapshot,
            DrugLicenseNoSnapshot = invoice.DrugLicenseNoSnapshot,
            TaxNumberSnapshot = invoice.TaxNumberSnapshot,
            ReceiptFooterSnapshot = invoice.ReceiptFooterSnapshot,
            Items = invoice.Items.Select(i => new SaleInvoiceItemDto
            {
                Id = i.Id,
                SaleInvoiceId = i.SaleInvoiceId,
                ProductId = i.ProductId,
                ProductCodeSnapshot = i.ProductCodeSnapshot,
                ProductNameSnapshot = i.ProductNameSnapshot,
                UnitSnapshot = i.UnitSnapshot,
                Quantity = i.Quantity,
                UnitSalePrice = i.UnitSalePrice,
                GrossAmount = i.GrossAmount,
                LineDiscountAmount = i.LineDiscountAmount,
                InvoiceDiscountAllocated = i.InvoiceDiscountAllocated,
                TaxAllocated = i.TaxAllocated,
                NetLineAmount = i.NetLineAmount,
                ReturnedQuantity = i.ReturnedQuantity,
                RefundedAmount = i.RefundedAmount,
                BatchAllocations = i.BatchAllocations.Select(b => new SaleInvoiceBatchAllocationDto
                {
                    Id = b.Id,
                    SaleInvoiceItemId = b.SaleInvoiceItemId,
                    ProductBatchId = b.ProductBatchId,
                    Quantity = b.Quantity,
                    InventoryCostRate = b.InventoryCostRate,
                    InventoryValueConsumed = b.InventoryValueConsumed,
                    BatchNumberSnapshot = b.BatchNumberSnapshot,
                    ExpiryDateSnapshot = b.ExpiryDateSnapshot,
                    ReturnedQuantity = b.ReturnedQuantity,
                    ReturnedInventoryValue = b.ReturnedInventoryValue
                }).ToList()
            }).ToList()
        };
    }
}
