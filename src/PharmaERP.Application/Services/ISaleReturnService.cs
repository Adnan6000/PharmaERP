using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public interface ISaleReturnService
{
    Task<SaleReturnDto> PostReturnAsync(SaleReturnCreateDto dto, CancellationToken cancellationToken = default);
    Task<SaleReturnDto> CancelReturnAsync(int id, string cancellationReason, CancellationToken cancellationToken = default);
    Task<SaleReturnDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<SaleReturnDto>> SearchReturnsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleReturnStatus? status,
        int take = 100,
        CancellationToken cancellationToken = default);
}

