using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public class InvoicePrintDataProvider : IInvoicePrintDataProvider
{
    private readonly ISaleInvoiceRepository _invoiceRepository;
    private readonly IAppConfigRepository _appConfigRepository;

    public InvoicePrintDataProvider(
        ISaleInvoiceRepository invoiceRepository,
        IAppConfigRepository appConfigRepository)
    {
        _invoiceRepository = invoiceRepository;
        _appConfigRepository = appConfigRepository;
    }

    public async Task<InvoicePrintDataDto> GetPrintDataAsync(int saleInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(saleInvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Sale invoice with ID {saleInvoiceId} was not found.");

        // Fetch current business profile as fallback if invoice snapshot is missing (for backwards compatibility)
        var fallbackProfile = await _appConfigRepository.GetBusinessProfileAsync(cancellationToken);

        var printData = new InvoicePrintDataDto
        {
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDateTimeUtc = invoice.InvoiceDateTimeUtc,
            BusinessDate = invoice.BusinessDate,
            CustomerName = !string.IsNullOrWhiteSpace(invoice.CustomerNameSnapshot)
                ? invoice.CustomerNameSnapshot
                : (invoice.Customer?.Name ?? "Walk-in Customer"),
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

            // Print Identity: strictly use persisted snapshot if present (Requirement 12)
            PharmacyName = !string.IsNullOrWhiteSpace(invoice.PharmacyNameSnapshot)
                ? invoice.PharmacyNameSnapshot
                : fallbackProfile.PharmacyName,
            PharmacyAddress = !string.IsNullOrWhiteSpace(invoice.PharmacyAddressSnapshot)
                ? invoice.PharmacyAddressSnapshot
                : fallbackProfile.Address,
            PharmacyPhone = !string.IsNullOrWhiteSpace(invoice.PharmacyPhoneSnapshot)
                ? invoice.PharmacyPhoneSnapshot
                : fallbackProfile.Phone,
            DrugLicenseNo = !string.IsNullOrWhiteSpace(invoice.DrugLicenseNoSnapshot)
                ? invoice.DrugLicenseNoSnapshot
                : fallbackProfile.DrugLicenseNumber,
            TaxNumber = !string.IsNullOrWhiteSpace(invoice.TaxNumberSnapshot)
                ? invoice.TaxNumberSnapshot
                : fallbackProfile.TaxNumber,
            ReceiptFooter = !string.IsNullOrWhiteSpace(invoice.ReceiptFooterSnapshot)
                ? invoice.ReceiptFooterSnapshot
                : fallbackProfile.ReceiptFooter,

            Items = invoice.Items.Select((item, idx) => new InvoicePrintItemDto
            {
                LineNumber = idx + 1,
                ProductCode = !string.IsNullOrWhiteSpace(item.ProductCodeSnapshot)
                    ? item.ProductCodeSnapshot
                    : (item.Product?.ProductCode ?? string.Empty),
                ProductName = !string.IsNullOrWhiteSpace(item.ProductNameSnapshot)
                    ? item.ProductNameSnapshot
                    : (item.Product?.Name ?? string.Empty),
                Unit = !string.IsNullOrWhiteSpace(item.UnitSnapshot)
                    ? item.UnitSnapshot
                    : (item.Product?.Unit?.Name ?? string.Empty),
                Quantity = item.Quantity,
                UnitSalePrice = item.UnitSalePrice,
                GrossAmount = item.GrossAmount,
                LineDiscountAmount = item.LineDiscountAmount,
                InvoiceDiscountAllocated = item.InvoiceDiscountAllocated,
                NetLineAmount = item.NetLineAmount,
                BatchAllocations = item.BatchAllocations.Select(b => new InvoicePrintAllocationDto
                {
                    BatchNumber = !string.IsNullOrWhiteSpace(b.BatchNumberSnapshot)
                        ? b.BatchNumberSnapshot
                        : (b.ProductBatch?.BatchNumber ?? string.Empty),
                    ExpiryDate = b.ExpiryDateSnapshot != default
                        ? b.ExpiryDateSnapshot
                        : (b.ProductBatch?.ExpiryDate ?? default),
                    Quantity = b.Quantity
                }).ToList()
            }).ToList()
        };

        return printData;
    }
}
