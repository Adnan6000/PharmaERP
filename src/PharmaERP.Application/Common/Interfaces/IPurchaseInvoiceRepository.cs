using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public record PurchaseInvoiceFilterQuery : PaginationQuery
{
    public int? SupplierId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public PurchaseInvoiceStatus? Status { get; init; }
    public string? SearchTerm { get; init; }
}

public interface IPurchaseInvoiceRepository
{
    Task<PagedResult<PurchaseInvoiceListDto>> GetPagedAsync(PurchaseInvoiceFilterQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ExistsSupplierInvoiceNumberAsync(int supplierId, string normalizedNumber, int? excludeId = null, CancellationToken cancellationToken = default);
}

