using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface ICustomerRepository
{
    Task<PagedResult<CustomerDto>> GetPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default);
}

