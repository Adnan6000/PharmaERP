using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public interface IInventoryService
{
    Task<ProductBatchDto> RecordOpeningStockAsync(OpeningStockCreateDto dto, CancellationToken cancellationToken = default);

    Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default);

    Task<PagedResult<StockMovementDto>> GetStockMovementsPagedAsync(PaginationQuery query, int? productId = null, int? batchId = null, CancellationToken cancellationToken = default);
}

public class InventoryService : IInventoryService
{
    private readonly IProductBatchRepository _batchRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IInventoryTransactionWriter _inventoryTransactionWriter;

    public InventoryService(
        IProductBatchRepository batchRepository,
        IStockMovementRepository movementRepository,
        IInventoryTransactionWriter inventoryTransactionWriter)
    {
        _batchRepository = batchRepository;
        _movementRepository = movementRepository;
        _inventoryTransactionWriter = inventoryTransactionWriter;
    }

    public async Task<ProductBatchDto> RecordOpeningStockAsync(OpeningStockCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.ProductId <= 0)
        {
            throw new ArgumentException("Valid product selection is required.", nameof(dto));
        }

        if (string.IsNullOrWhiteSpace(dto.BatchNumber))
        {
            throw new ArgumentException("Batch number is required.", nameof(dto));
        }

        if (dto.Quantity <= 0)
        {
            throw new ArgumentException("Opening stock quantity must be greater than zero.", nameof(dto));
        }

        if (dto.UnitCost < 0)
        {
            throw new ArgumentException("Unit cost cannot be negative.", nameof(dto));
        }

        if (dto.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            throw new ArgumentException("Cannot record opening stock with an already expired date.", nameof(dto));
        }

        return await _inventoryTransactionWriter.RecordOpeningStockAsync(dto, cancellationToken);
    }

    public async Task<PagedResult<BatchStockDto>> GetBatchStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? productId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _batchRepository.GetBatchStockPagedAsync(query, searchTerm, productId, cancellationToken);
    }

    public async Task<PagedResult<CurrentStockDto>> GetCurrentStockPagedAsync(PaginationQuery query, string? searchTerm = null, int? categoryId = null, int? manufacturerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _batchRepository.GetCurrentStockPagedAsync(query, searchTerm, categoryId, manufacturerId, cancellationToken);
    }

    public async Task<PagedResult<ExpiryReportItemDto>> GetExpiryReportPagedAsync(PaginationQuery query, int nearExpiryDaysThreshold = 90, string? filterStatus = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _batchRepository.GetExpiryReportPagedAsync(query, nearExpiryDaysThreshold, filterStatus, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductBatchDto>> GetActiveFefoBatchesAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) return [];
        return await _batchRepository.GetActiveFefoBatchesAsync(productId, cancellationToken);
    }

    public async Task<PagedResult<StockMovementDto>> GetStockMovementsPagedAsync(PaginationQuery query, int? productId = null, int? batchId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _movementRepository.GetPagedAsync(query, productId, batchId, cancellationToken);
    }
}

