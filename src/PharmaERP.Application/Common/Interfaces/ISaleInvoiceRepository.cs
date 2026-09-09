using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Common.Interfaces;

public interface ISaleInvoiceRepository
{
    Task<SaleInvoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<SaleInvoice?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<SaleInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default);
    Task<List<SaleInvoiceDto>> GetInvoicesAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? customerId,
        SaleType? saleType,
        SaleInvoiceStatus? status,
        string? searchTerm,
        int take = 100,
        CancellationToken cancellationToken = default);
}

