using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Common.Interfaces;

public interface ISaleReturnRepository
{
    Task<SaleReturn?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<SaleReturn?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<SaleReturn?> GetByReturnNumberAsync(string returnNumber, CancellationToken cancellationToken = default);
    Task<List<SaleReturnDto>> GetReturnsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleReturnStatus? status,
        int take = 100,
        CancellationToken cancellationToken = default);
}

