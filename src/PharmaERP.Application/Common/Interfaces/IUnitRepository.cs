using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface IUnitRepository
{
    Task<IReadOnlyList<UnitDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Unit?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Unit> AddAsync(Unit unit, CancellationToken cancellationToken = default);

    Task UpdateAsync(Unit unit, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsAbbreviationAsync(string abbreviation, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default);
}

