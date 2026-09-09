using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public interface IInvoicePrintDataProvider
{
    Task<InvoicePrintDataDto> GetPrintDataAsync(int saleInvoiceId, CancellationToken cancellationToken = default);
}

