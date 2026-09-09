using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Category> AddAsync(Category category, CancellationToken cancellationToken = default);

    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default);
}

