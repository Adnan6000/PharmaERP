using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class SaleReturnService : ISaleReturnService
{
    private readonly ISaleTransactionWriter _transactionWriter;
    private readonly ISaleReturnRepository _returnRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;

    public SaleReturnService(
        ISaleTransactionWriter transactionWriter,
        ISaleReturnRepository returnRepository,
        IDocumentNumberGenerator documentNumberGenerator)
    {
        _transactionWriter = transactionWriter;
        _returnRepository = returnRepository;
        _documentNumberGenerator = documentNumberGenerator;
    }

    public async Task<SaleReturnDto> PostReturnAsync(SaleReturnCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Check if return for this OperationId was already committed (Idempotency Recovery - Requirement 11)
        var existing = await _returnRepository.GetByOperationIdAsync(dto.OperationId, cancellationToken);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        string returnNumber = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.SaleReturn, cancellationToken: cancellationToken);
        return await _transactionWriter.CreateAndPostSaleReturnAsync(returnNumber, dto, cancellationToken);
    }

    public async Task<SaleReturnDto> CancelReturnAsync(int id, string cancellationReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
        {
            throw new ValidationException("Cancellation reason is required.");
        }

        return await _transactionWriter.CancelSaleReturnAsync(id, cancellationReason.Trim(), cancellationToken);
    }

    public async Task<SaleReturnDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var ret = await _returnRepository.GetByIdAsync(id, cancellationToken);
        return ret == null ? null : MapToDto(ret);
    }

    public async Task<List<SaleReturnDto>> SearchReturnsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleReturnStatus? status,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _returnRepository.GetReturnsAsync(fromDate, toDate, customerId, status, take, cancellationToken);
    }

    private static SaleReturnDto MapToDto(Domain.Entities.SaleReturn ret)
    {
        return new SaleReturnDto
        {
            Id = ret.Id,
            ReturnNumber = ret.ReturnNumber,
            OperationId = ret.OperationId,
            OriginalSaleInvoiceId = ret.OriginalSaleInvoiceId,
            OriginalSaleInvoiceNumber = ret.OriginalSaleInvoice?.InvoiceNumber ?? string.Empty,
            CustomerId = ret.CustomerId,
            CustomerNameSnapshot = ret.CustomerNameSnapshot,
            ReturnDate = ret.ReturnDate,
            Reason = ret.Reason,
            TotalAmount = ret.TotalAmount,
            Status = ret.Status,
            PostedAtUtc = ret.PostedAtUtc,
            CancelledAtUtc = ret.CancelledAtUtc,
            CancellationReason = ret.CancellationReason,
            Items = ret.Items.Select(i => new SaleReturnItemDto
            {
                Id = i.Id,
                SaleReturnId = i.SaleReturnId,
                OriginalSaleInvoiceItemId = i.OriginalSaleInvoiceItemId,
                ProductId = i.ProductId,
                ProductName = i.OriginalSaleInvoiceItem?.ProductNameSnapshot ?? string.Empty,
                Quantity = i.Quantity,
                RefundAmount = i.RefundAmount,
                BatchAllocations = i.BatchAllocations.Select(b => new SaleReturnBatchAllocationDto
                {
                    Id = b.Id,
                    SaleReturnItemId = b.SaleReturnItemId,
                    OriginalSaleInvoiceBatchAllocationId = b.OriginalSaleInvoiceBatchAllocationId,
                    ProductBatchId = b.ProductBatchId,
                    BatchNumber = b.OriginalSaleInvoiceBatchAllocation?.BatchNumberSnapshot ?? string.Empty,
                    Quantity = b.Quantity,
                    RestoredInventoryCostRate = b.RestoredInventoryCostRate,
                    RestoredInventoryValue = b.RestoredInventoryValue
                }).ToList()
            }).ToList()
        };
    }
}
