using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<CategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateAsync(CategoryUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);
}

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repository;

    public CategoryService(ICategoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync(searchTerm, cancellationToken);
    }

    public async Task<CategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : new CategoryDto(entity.Id, entity.Name, entity.Description, entity.IsActive, entity.RowVersion);
    }

    public async Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsNameAsync(dto.Name.Trim(), null, cancellationToken))
        {
            throw new DuplicateKeyException($"Category with name '{dto.Name}' already exists.", nameof(dto.Name));
        }

        var entity = new Category
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            IsActive = dto.IsActive
        };

        var created = await _repository.AddAsync(entity, cancellationToken);
        return new CategoryDto(created.Id, created.Name, created.Description, created.IsActive, created.RowVersion);
    }

    public async Task UpdateAsync(CategoryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(dto));
        }

        if (await _repository.ExistsNameAsync(dto.Name.Trim(), dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Category with name '{dto.Name}' already exists.", nameof(dto.Name));
        }

        var entity = await _repository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Category with ID {dto.Id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.IsActive = dto.IsActive;
        entity.RowVersion = dto.RowVersion;

        await _repository.UpdateAsync(entity, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _repository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }
}

