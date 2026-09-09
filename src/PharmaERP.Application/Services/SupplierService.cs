using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface ISupplierService
{
    Task<PagedResult<SupplierDto>> GetSuppliersPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<SupplierDto?> GetSupplierByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<SupplierDto> CreateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupsAsync(CancellationToken cancellationToken = default);
}

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;

    public SupplierService(
        ISupplierRepository supplierRepository,
        IDocumentNumberGenerator documentNumberGenerator)
    {
        _supplierRepository = supplierRepository;
        _documentNumberGenerator = documentNumberGenerator;
    }

    public async Task<PagedResult<SupplierDto>> GetSuppliersPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _supplierRepository.GetPagedAsync(query, searchTerm, cancellationToken);
    }

    public async Task<SupplierDto?> GetSupplierByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _supplierRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return null;

        return new SupplierDto(
            entity.Id,
            entity.SupplierCode,
            entity.Name,
            entity.ContactPerson,
            entity.Phone,
            entity.Address,
            entity.IsActive,
            entity.RowVersion);
    }

    public async Task<SupplierDto> CreateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Supplier name is required.", nameof(dto));
        }

        string code = dto.SupplierCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.Supplier, cancellationToken: cancellationToken);
        }
        else
        {
            if (await _supplierRepository.ExistsCodeAsync(code, null, cancellationToken))
            {
                throw new DuplicateKeyException($"Supplier code '{code}' already exists.", nameof(dto.SupplierCode));
            }
        }

        var supplier = new Supplier
        {
            SupplierCode = code,
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson?.Trim(),
            Phone = dto.Phone?.Trim(),
            Address = dto.Address?.Trim(),
            IsActive = dto.IsActive
        };

        var created = await _supplierRepository.AddAsync(supplier, cancellationToken);

        return new SupplierDto(
            created.Id,
            created.SupplierCode,
            created.Name,
            created.ContactPerson,
            created.Phone,
            created.Address,
            created.IsActive,
            created.RowVersion);
    }

    public async Task UpdateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Supplier name is required.", nameof(dto));
        }

        var existing = await _supplierRepository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Supplier with ID {dto.Id} was not found.");

        string code = dto.SupplierCode?.Trim() ?? existing.SupplierCode;
        if (await _supplierRepository.ExistsCodeAsync(code, dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Supplier code '{code}' already exists.", nameof(dto.SupplierCode));
        }

        existing.SupplierCode = code;
        existing.Name = dto.Name.Trim();
        existing.ContactPerson = dto.ContactPerson?.Trim();
        existing.Phone = dto.Phone?.Trim();
        existing.Address = dto.Address?.Trim();
        existing.IsActive = dto.IsActive;
        existing.RowVersion = dto.RowVersion;

        await _supplierRepository.UpdateAsync(existing, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _supplierRepository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupsAsync(CancellationToken cancellationToken = default)
    {
        return await _supplierRepository.GetActiveLookupAsync(cancellationToken);
    }
}

