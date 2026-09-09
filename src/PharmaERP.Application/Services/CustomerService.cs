using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> GetCustomersPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<CustomerDto?> GetCustomerByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<CustomerDto> CreateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default);

    Task UpdateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetActiveLookupsAsync(CancellationToken cancellationToken = default);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;

    public CustomerService(
        ICustomerRepository customerRepository,
        IDocumentNumberGenerator documentNumberGenerator)
    {
        _customerRepository = customerRepository;
        _documentNumberGenerator = documentNumberGenerator;
    }

    public async Task<PagedResult<CustomerDto>> GetCustomersPagedAsync(PaginationQuery query, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _customerRepository.GetPagedAsync(query, searchTerm, cancellationToken);
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _customerRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return null;

        return new CustomerDto(
            entity.Id,
            entity.CustomerCode,
            entity.Name,
            entity.ContactPerson,
            entity.Phone,
            entity.Address,
            entity.CreditLimit,
            entity.IsActive,
            entity.RowVersion);
    }

    public async Task<CustomerDto> CreateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Customer name is required.", nameof(dto));
        }

        if (dto.CreditLimit < 0)
        {
            throw new ArgumentException("Credit limit cannot be negative.", nameof(dto));
        }

        string code = dto.CustomerCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.Customer, cancellationToken: cancellationToken);
        }
        else
        {
            if (await _customerRepository.ExistsCodeAsync(code, null, cancellationToken))
            {
                throw new DuplicateKeyException($"Customer code '{code}' already exists.", nameof(dto.CustomerCode));
            }
        }

        var customer = new Customer
        {
            CustomerCode = code,
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson?.Trim(),
            Phone = dto.Phone?.Trim(),
            Address = dto.Address?.Trim(),
            CreditLimit = dto.CreditLimit,
            IsActive = dto.IsActive
        };

        var created = await _customerRepository.AddAsync(customer, cancellationToken);

        return new CustomerDto(
            created.Id,
            created.CustomerCode,
            created.Name,
            created.ContactPerson,
            created.Phone,
            created.Address,
            created.CreditLimit,
            created.IsActive,
            created.RowVersion);
    }

    public async Task UpdateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Customer name is required.", nameof(dto));
        }

        if (dto.CreditLimit < 0)
        {
            throw new ArgumentException("Credit limit cannot be negative.", nameof(dto));
        }

        var existing = await _customerRepository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Customer with ID {dto.Id} was not found.");

        string code = dto.CustomerCode?.Trim() ?? existing.CustomerCode;
        if (await _customerRepository.ExistsCodeAsync(code, dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Customer code '{code}' already exists.", nameof(dto.CustomerCode));
        }

        existing.CustomerCode = code;
        existing.Name = dto.Name.Trim();
        existing.ContactPerson = dto.ContactPerson?.Trim();
        existing.Phone = dto.Phone?.Trim();
        existing.Address = dto.Address?.Trim();
        existing.CreditLimit = dto.CreditLimit;
        existing.IsActive = dto.IsActive;
        existing.RowVersion = dto.RowVersion;

        await _customerRepository.UpdateAsync(existing, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _customerRepository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupDto>> GetActiveLookupsAsync(CancellationToken cancellationToken = default)
    {
        return await _customerRepository.GetActiveLookupAsync(cancellationToken);
    }
}

