using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IPurchaseTransactionWriter
{
    Task<PurchaseInvoiceDto> CreateAndPostPurchaseInvoiceAsync(
        string invoiceNumber,
        PurchaseInvoiceCreateDto dto,
        CancellationToken cancellationToken = default);

    Task CancelPurchaseInvoiceAsync(
        int purchaseInvoiceId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnDto> CreateAndPostPurchaseReturnAsync(
        string returnNumber,
        PurchaseReturnCreateDto dto,
        CancellationToken cancellationToken = default);

    Task CancelPurchaseReturnAsync(
        int purchaseReturnId,
        string reason,
        CancellationToken cancellationToken = default);
}


