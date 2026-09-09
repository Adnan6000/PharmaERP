using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface ISupplierRepository
{
    Task<PagedResult<SupplierDto>> GetPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Supplier> AddAsync(Supplier supplier, CancellationToken cancellationToken = default);

    Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default);
}

