using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public record PurchaseReturnFilterQuery : PaginationQuery
{
    public int? SupplierId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public PurchaseReturnStatus? Status { get; init; }
    public string? SearchTerm { get; init; }
}

public interface IPurchaseReturnRepository
{
    Task<PagedResult<PurchaseReturnListDto>> GetPagedAsync(PurchaseReturnFilterQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseReturnDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}

