using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface IUnitService
{
    Task<IReadOnlyList<UnitDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<UnitDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<UnitDto> CreateAsync(UnitUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateAsync(UnitUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);
}

public class UnitService : IUnitService
{
    private readonly IUnitRepository _repository;

    public UnitService(IUnitRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<UnitDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync(searchTerm, cancellationToken);
    }

    public async Task<UnitDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : new UnitDto(entity.Id, entity.Name, entity.Abbreviation, entity.IsActive, entity.RowVersion);
    }

    public async Task<UnitDto> CreateAsync(UnitUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Unit name cannot be empty.", nameof(dto));
        }
        if (string.IsNullOrWhiteSpace(dto.Abbreviation))
        {
            throw new ArgumentException("Unit abbreviation cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsAbbreviationAsync(dto.Abbreviation.Trim(), null, cancellationToken))
        {
            throw new DuplicateKeyException($"Unit with abbreviation '{dto.Abbreviation}' already exists.", nameof(dto.Abbreviation));
        }

        var entity = new Unit
        {
            Name = dto.Name.Trim(),
            Abbreviation = dto.Abbreviation.Trim().ToUpperInvariant(),
            IsActive = dto.IsActive
        };

        var created = await _repository.AddAsync(entity, cancellationToken);
        return new UnitDto(created.Id, created.Name, created.Abbreviation, created.IsActive, created.RowVersion);
    }

    public async Task UpdateAsync(UnitUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Unit name cannot be empty.", nameof(dto));
        }
        if (string.IsNullOrWhiteSpace(dto.Abbreviation))
        {
            throw new ArgumentException("Unit abbreviation cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsAbbreviationAsync(dto.Abbreviation.Trim(), dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Unit with abbreviation '{dto.Abbreviation}' already exists.", nameof(dto.Abbreviation));
        }

        var entity = await _repository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Unit with ID {dto.Id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.Abbreviation = dto.Abbreviation.Trim().ToUpperInvariant();
        entity.IsActive = dto.IsActive;
        entity.RowVersion = dto.RowVersion;

        await _repository.UpdateAsync(entity, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _repository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }
}

