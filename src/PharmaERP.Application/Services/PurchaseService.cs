using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public interface IPurchaseService
{
    Task<PurchaseInvoiceDto> CreateAndPostPurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, CancellationToken cancellationToken = default);

    Task CancelPurchaseInvoiceAsync(int invoiceId, string reason, CancellationToken cancellationToken = default);

    Task<PurchaseReturnDto> CreateAndPostPurchaseReturnAsync(PurchaseReturnCreateDto dto, CancellationToken cancellationToken = default);

    Task CancelPurchaseReturnAsync(int returnId, string reason, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseInvoiceListDto>> GetPurchaseInvoicesPagedAsync(PurchaseInvoiceFilterQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceDto?> GetPurchaseInvoiceByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseReturnListDto>> GetPurchaseReturnsPagedAsync(PurchaseReturnFilterQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseReturnDto?> GetPurchaseReturnByIdAsync(int id, CancellationToken cancellationToken = default);
}

public class PurchaseService : IPurchaseService
{
    private readonly IPurchaseInvoiceRepository _invoiceRepository;
    private readonly IPurchaseReturnRepository _returnRepository;
    private readonly IPurchaseTransactionWriter _purchaseTransactionWriter;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;

    public PurchaseService(
        IPurchaseInvoiceRepository invoiceRepository,
        IPurchaseReturnRepository returnRepository,
        IPurchaseTransactionWriter purchaseTransactionWriter,
        IDocumentNumberGenerator documentNumberGenerator)
    {
        _invoiceRepository = invoiceRepository;
        _returnRepository = returnRepository;
        _purchaseTransactionWriter = purchaseTransactionWriter;
        _documentNumberGenerator = documentNumberGenerator;
    }

    public async Task<PurchaseInvoiceDto> CreateAndPostPurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.SupplierId <= 0)
        {
            throw new ArgumentException("Valid supplier selection is required.", nameof(dto));
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Purchase invoice must contain at least one line item.", nameof(dto));
        }

        // Validate individual line items
        var batchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dto.Items)
        {
            if (item.ProductId <= 0)
            {
                throw new ArgumentException("Each item must have a valid product selected.", nameof(dto));
            }

            if (string.IsNullOrWhiteSpace(item.BatchNumber))
            {
                throw new ArgumentException("Batch number is required on every item.", nameof(dto));
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.", nameof(dto));
            }

            if (item.PurchaseRate < 0)
            {
                throw new ArgumentException("Purchase rate cannot be negative.", nameof(dto));
            }

            if (item.DiscountAmount < 0)
            {
                throw new ArgumentException("Discount cannot be negative.", nameof(dto));
            }

            if (item.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today))
            {
                throw new ArgumentException($"Cannot purchase already expired stock for batch '{item.BatchNumber}'.", nameof(dto));
            }

            // Duplicate line validation within the invoice
            string key = $"{item.ProductId}:{item.BatchNumber.Trim().ToUpperInvariant()}:{item.ExpiryDate:yyyy-MM-dd}";
            if (!batchKeys.Add(key))
            {
                throw new ArgumentException($"Duplicate line for batch '{item.BatchNumber}'. Please combine quantities into a single line.", nameof(dto));
            }
        }

        // Duplicate supplier bill pre-check
        if (!string.IsNullOrWhiteSpace(dto.SupplierInvoiceNumber))
        {
            string norm = dto.SupplierInvoiceNumber.Trim().ToUpperInvariant();
            if (await _invoiceRepository.ExistsSupplierInvoiceNumberAsync(dto.SupplierId, norm, null, cancellationToken))
            {
                throw new DuplicateKeyException($"Supplier bill number '{dto.SupplierInvoiceNumber}' already exists for this supplier.", nameof(dto.SupplierInvoiceNumber));
            }
        }

        // Allocate unique document sequence in short transaction prior to write
        var invoiceNumber = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.PurchaseInvoice, dto.InvoiceDate.Year, cancellationToken);

        // Execute atomic multi-table write inside Infrastructure
        return await _purchaseTransactionWriter.CreateAndPostPurchaseInvoiceAsync(invoiceNumber, dto, cancellationToken);
    }

    public async Task CancelPurchaseInvoiceAsync(int invoiceId, string reason, CancellationToken cancellationToken = default)
    {
        if (invoiceId <= 0)
        {
            throw new ArgumentException("Valid invoice ID is required.", nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));
        }

        await _purchaseTransactionWriter.CancelPurchaseInvoiceAsync(invoiceId, reason.Trim(), cancellationToken);
    }

    public async Task<PurchaseReturnDto> CreateAndPostPurchaseReturnAsync(PurchaseReturnCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.SupplierId <= 0)
        {
            throw new ArgumentException("Valid supplier selection is required.", nameof(dto));
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Purchase return must contain at least one line item.", nameof(dto));
        }

        var returnBatches = new HashSet<int>();
        foreach (var item in dto.Items)
        {
            if (item.ProductId <= 0 || item.ProductBatchId <= 0)
            {
                throw new ArgumentException("Each item must have a valid product and batch selected.", nameof(dto));
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Return quantity must be greater than zero.", nameof(dto));
            }

            if (!returnBatches.Add(item.ProductBatchId))
            {
                throw new ArgumentException("Duplicate batch in return lines. Please combine quantities.", nameof(dto));
            }
        }

        // Allocate return number
        var returnNumber = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.PurchaseReturn, dto.ReturnDate.Year, cancellationToken);

        return await _purchaseTransactionWriter.CreateAndPostPurchaseReturnAsync(returnNumber, dto, cancellationToken);
    }

    public async Task CancelPurchaseReturnAsync(int returnId, string reason, CancellationToken cancellationToken = default)
    {
        if (returnId <= 0)
        {
            throw new ArgumentException("Valid purchase return ID is required.", nameof(returnId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));
        }

        await _purchaseTransactionWriter.CancelPurchaseReturnAsync(returnId, reason.Trim(), cancellationToken);
    }

    public async Task<PagedResult<PurchaseInvoiceListDto>> GetPurchaseInvoicesPagedAsync(PurchaseInvoiceFilterQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _invoiceRepository.GetPagedAsync(query, cancellationToken);
    }

    public async Task<PurchaseInvoiceDto?> GetPurchaseInvoiceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        return await _invoiceRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<PagedResult<PurchaseReturnListDto>> GetPurchaseReturnsPagedAsync(PurchaseReturnFilterQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _returnRepository.GetPagedAsync(query, cancellationToken);
    }

    public async Task<PurchaseReturnDto?> GetPurchaseReturnByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        return await _returnRepository.GetByIdAsync(id, cancellationToken);
    }
}

