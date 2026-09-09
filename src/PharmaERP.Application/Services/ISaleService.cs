using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public interface ISaleService
{
    Task<SalePreviewDto> PreviewSaleAsync(SaleInvoiceCreateDto dto, CancellationToken cancellationToken = default);
    Task<SaleInvoiceDto> PostSaleAsync(SaleInvoiceCreateDto dto, CancellationToken cancellationToken = default);
    Task<SaleInvoiceDto> CancelSaleAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<SaleInvoiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<SaleInvoiceDto>> SearchInvoicesAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleType? saleType,
        SaleInvoiceStatus? status,
        string? searchTerm,
        int take = 100,
        CancellationToken cancellationToken = default);
}

