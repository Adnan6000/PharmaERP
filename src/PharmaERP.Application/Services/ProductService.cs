using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Services;

/// <summary>
/// Domain application service handling product operations.
/// Completely decoupled from persistence/EF Core abstractions.
/// </summary>
public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IManufacturerRepository _manufacturerRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitRepository _unitRepository;

    public ProductService(
        IProductRepository productRepository,
        IManufacturerRepository manufacturerRepository,
        ICategoryRepository categoryRepository,
        IUnitRepository unitRepository)
    {
        _productRepository = productRepository;
        _manufacturerRepository = manufacturerRepository;
        _categoryRepository = categoryRepository;
        _unitRepository = unitRepository;
    }

    public async Task<PagedResult<ProductDto>> GetProductsPagedAsync(
        PaginationQuery query,
        string? searchTerm = null,
        int? categoryId = null,
        int? manufacturerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _productRepository.GetPagedAsync(query, searchTerm, categoryId, manufacturerId, cancellationToken);
    }

    public async Task<ProductDto?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var entity = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            return null;
        }

        return new ProductDto(
            entity.Id,
            entity.Name,
            entity.GenericName,
            entity.ProductCode,
            entity.Barcode,
            entity.DefaultPurchasePrice,
            entity.DefaultSalePrice,
            entity.IsActive,
            entity.CategoryId,
            entity.Category?.Name,
            entity.ManufacturerId,
            entity.Manufacturer?.Name,
            entity.UnitId,
            entity.Unit?.Abbreviation,
            entity.RowVersion);
    }

    public async Task<ProductDto> CreateProductAsync(ProductUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        ValidateUpsert(dto);

        if (!string.IsNullOrWhiteSpace(dto.ProductCode) &&
            await _productRepository.ExistsCodeAsync(dto.ProductCode.Trim(), null, cancellationToken))
        {
            throw new DuplicateKeyException($"Product code '{dto.ProductCode}' already exists.", nameof(dto.ProductCode));
        }

        if (!string.IsNullOrWhiteSpace(dto.Barcode) &&
            await _productRepository.ExistsBarcodeAsync(dto.Barcode.Trim(), null, cancellationToken))
        {
            throw new DuplicateKeyException($"Barcode '{dto.Barcode}' already exists.", nameof(dto.Barcode));
        }

        var product = new Product
        {
            Name = dto.Name.Trim(),
            GenericName = dto.GenericName?.Trim(),
            ProductCode = dto.ProductCode?.Trim(),
            Barcode = dto.Barcode?.Trim(),
            DefaultPurchasePrice = dto.DefaultPurchasePrice,
            DefaultSalePrice = dto.DefaultSalePrice,
            IsActive = dto.IsActive,
            CategoryId = dto.CategoryId,
            ManufacturerId = dto.ManufacturerId,
            UnitId = dto.UnitId
        };

        var created = await _productRepository.AddAsync(product, cancellationToken);

        return new ProductDto(
            created.Id,
            created.Name,
            created.GenericName,
            created.ProductCode,
            created.Barcode,
            created.DefaultPurchasePrice,
            created.DefaultSalePrice,
            created.IsActive,
            created.CategoryId,
            created.Category?.Name,
            created.ManufacturerId,
            created.Manufacturer?.Name,
            created.UnitId,
            created.Unit?.Abbreviation,
            created.RowVersion);
    }

    public async Task UpdateProductAsync(ProductUpsertDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        ValidateUpsert(dto);

        if (!string.IsNullOrWhiteSpace(dto.ProductCode) &&
            await _productRepository.ExistsCodeAsync(dto.ProductCode.Trim(), dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Product code '{dto.ProductCode}' already exists.", nameof(dto.ProductCode));
        }

        if (!string.IsNullOrWhiteSpace(dto.Barcode) &&
            await _productRepository.ExistsBarcodeAsync(dto.Barcode.Trim(), dto.Id, cancellationToken))
        {
            throw new DuplicateKeyException($"Barcode '{dto.Barcode}' already exists.", nameof(dto.Barcode));
        }

        var entity = await _productRepository.GetByIdAsync(dto.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Product with ID {dto.Id} was not found.");

        entity.Name = dto.Name.Trim();
        entity.GenericName = dto.GenericName?.Trim();
        entity.ProductCode = dto.ProductCode?.Trim();
        entity.Barcode = dto.Barcode?.Trim();
        entity.DefaultPurchasePrice = dto.DefaultPurchasePrice;
        entity.DefaultSalePrice = dto.DefaultSalePrice;
        entity.IsActive = dto.IsActive;
        entity.CategoryId = dto.CategoryId;
        entity.ManufacturerId = dto.ManufacturerId;
        entity.UnitId = dto.UnitId;
        entity.RowVersion = dto.RowVersion;

        await _productRepository.UpdateAsync(entity, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        return await _productRepository.ToggleActiveAsync(id, rowVersion, cancellationToken);
    }

    public async Task<ProductLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var manufacturers = await _manufacturerRepository.GetActiveLookupAsync(cancellationToken);
        var categories = await _categoryRepository.GetActiveLookupAsync(cancellationToken);
        var units = await _unitRepository.GetActiveLookupAsync(cancellationToken);

        return new ProductLookupsDto(manufacturers, categories, units);
    }

    private static void ValidateUpsert(ProductUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Product name cannot be empty.", nameof(dto));
        }

        if (dto.DefaultPurchasePrice < 0)
        {
            throw new ArgumentException("Default purchase price cannot be negative.", nameof(dto));
        }

        if (dto.DefaultSalePrice < 0)
        {
            throw new ArgumentException("Default sale price cannot be negative.", nameof(dto));
        }
    }
}

