using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface ISettlementRepository
{
    Task<ReceiptVoucherAllocationDto> AllocateReceiptAsync(ReceiptAllocationCreateDto dto, CancellationToken cancellationToken = default);
    Task<PaymentVoucherAllocationDto> AllocatePaymentAsync(PaymentAllocationCreateDto dto, CancellationToken cancellationToken = default);
    Task<List<OpenSaleInvoiceDto>> GetCustomerOpenInvoicesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<List<OpenPurchaseInvoiceDto>> GetSupplierOpenInvoicesAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<List<ReceiptVoucherAllocationDto>> GetReceiptVoucherAllocationsAsync(int receiptVoucherId, CancellationToken cancellationToken = default);
    Task<List<PaymentVoucherAllocationDto>> GetPaymentVoucherAllocationsAsync(int paymentVoucherId, CancellationToken cancellationToken = default);
    Task<ReceiptVoucherAllocationDto> VoidReceiptAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default);
    Task<PaymentVoucherAllocationDto> VoidPaymentAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default);
}

