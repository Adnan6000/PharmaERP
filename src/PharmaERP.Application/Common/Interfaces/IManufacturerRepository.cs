using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface IManufacturerRepository
{
    Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Manufacturer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Manufacturer> AddAsync(Manufacturer manufacturer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Manufacturer manufacturer, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupAsync(CancellationToken cancellationToken = default);
}
