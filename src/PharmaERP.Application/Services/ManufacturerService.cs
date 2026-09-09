using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface IManufacturerService
{
    Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<ManufacturerDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ManufacturerDto> CreateAsync(ManufacturerUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateAsync(ManufacturerUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);
}

public class ManufacturerService : IManufacturerService
{
    private readonly IManufacturerRepository _repository;

    public ManufacturerService(IManufacturerRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ManufacturerDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync(searchTerm, cancellationToken);
    }

    public async Task<ManufacturerDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : new ManufacturerDto(entity.Id, entity.Name, entity.ContactPerson, entity.Phone, entity.Email, entity.IsActive, entity.RowVersion);
    }

    public async Task<ManufacturerDto> CreateAsync(ManufacturerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Manufacturer name cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsNameAsync(dto.Name.Trim(), null, cancellationToken))
        {
            throw new DuplicateKeyException($"Manufacturer with name '{dto.Name}' already exists.", nameof(dto.Name));
        }

        var entity = new Manufacturer
        {
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson?.Trim(),
            Phone = dto.Phone?.Trim(),
            Email = dto.Email?.Trim(),
            IsActive = dto.IsActive
        };

        var created = await _repository.AddAsync(entity, cancellationToken);
        return new ManufacturerDto(created.Id, created.Name, created.ContactPerson, created.Phone, created.Email, created.IsActive, created.RowVersion);
    }

    public async Task UpdateAsync(ManufacturerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Manufacturer name cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsNameAsync(dto.Name.Trim(), dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Manufacturer with name '{dto.Name}' already exists.", nameof(dto.Name));
        }

        var entity = await _repository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Manufacturer with ID {dto.Id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.ContactPerson = dto.ContactPerson?.Trim();
        entity.Phone = dto.Phone?.Trim();
        entity.Email = dto.Email?.Trim();
        entity.IsActive = dto.IsActive;
        entity.RowVersion = dto.RowVersion;

        await _repository.UpdateAsync(entity, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _repository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }
}
