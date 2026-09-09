using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface ISaleTransactionWriter
{
    Task<SaleInvoiceDto> CreateAndPostSaleInvoiceAsync(
        string invoiceNumber,
        SaleInvoiceCreateDto dto,
        CancellationToken cancellationToken = default);

    Task<SaleReturnDto> CreateAndPostSaleReturnAsync(
        string returnNumber,
        SaleReturnCreateDto dto,
        CancellationToken cancellationToken = default);

    Task<SaleInvoiceDto> CancelSaleInvoiceAsync(
        int id,
        string cancellationReason,
        CancellationToken cancellationToken = default);

    Task<SaleReturnDto> CancelSaleReturnAsync(
        int id,
        string cancellationReason,
        CancellationToken cancellationToken = default);
}

