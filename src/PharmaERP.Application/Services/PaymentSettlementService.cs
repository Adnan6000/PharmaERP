using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;

namespace PharmaERP.Application.Services;

public class PaymentSettlementService : IPaymentSettlementService
{
    private readonly ISettlementRepository _settlementRepository;

    public PaymentSettlementService(ISettlementRepository settlementRepository)
    {
        _settlementRepository = settlementRepository;
    }

    public async Task<ReceiptVoucherAllocationDto> AllocateReceiptVoucherAsync(ReceiptAllocationCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.ReceiptVoucherId <= 0)
            throw new ValidationException("Invalid Receipt Voucher ID.");
        if (dto.SaleInvoiceId <= 0)
            throw new ValidationException("Invalid Sale Invoice ID.");
        if (dto.AllocatedAmount <= 0)
            throw new ValidationException("Allocated amount must be strictly positive.");

        return await _settlementRepository.AllocateReceiptAsync(dto, cancellationToken);
    }

    public async Task<PaymentVoucherAllocationDto> AllocatePaymentVoucherAsync(PaymentAllocationCreateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.PaymentVoucherId <= 0)
            throw new ValidationException("Invalid Payment Voucher ID.");
        if (dto.PurchaseInvoiceId <= 0)
            throw new ValidationException("Invalid Purchase Invoice ID.");
        if (dto.AllocatedAmount <= 0)
            throw new ValidationException("Allocated amount must be strictly positive.");

        return await _settlementRepository.AllocatePaymentAsync(dto, cancellationToken);
    }

    public async Task<List<OpenSaleInvoiceDto>> GetCustomerOpenInvoicesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
            throw new ValidationException("Invalid Customer ID.");

        return await _settlementRepository.GetCustomerOpenInvoicesAsync(customerId, cancellationToken);
    }

    public async Task<List<OpenPurchaseInvoiceDto>> GetSupplierOpenInvoicesAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        if (supplierId <= 0)
            throw new ValidationException("Invalid Supplier ID.");

        return await _settlementRepository.GetSupplierOpenInvoicesAsync(supplierId, cancellationToken);
    }

    public async Task<List<ReceiptVoucherAllocationDto>> GetReceiptVoucherAllocationsAsync(int receiptVoucherId, CancellationToken cancellationToken = default)
    {
        if (receiptVoucherId <= 0)
            throw new ValidationException("Invalid Receipt Voucher ID.");

        return await _settlementRepository.GetReceiptVoucherAllocationsAsync(receiptVoucherId, cancellationToken);
    }

    public async Task<List<PaymentVoucherAllocationDto>> GetPaymentVoucherAllocationsAsync(int paymentVoucherId, CancellationToken cancellationToken = default)
    {
        if (paymentVoucherId <= 0)
            throw new ValidationException("Invalid Payment Voucher ID.");

        return await _settlementRepository.GetPaymentVoucherAllocationsAsync(paymentVoucherId, cancellationToken);
    }

    public async Task<ReceiptVoucherAllocationDto> VoidReceiptAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default)
    {
        if (allocationId <= 0)
            throw new ValidationException("Invalid allocation ID.");
        if (string.IsNullOrWhiteSpace(voidReason))
            throw new ValidationException("Void reason is mandatory.");

        return await _settlementRepository.VoidReceiptAllocationAsync(allocationId, voidReason.Trim(), cancellationToken);
    }

    public async Task<PaymentVoucherAllocationDto> VoidPaymentAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default)
    {
        if (allocationId <= 0)
            throw new ValidationException("Invalid allocation ID.");
        if (string.IsNullOrWhiteSpace(voidReason))
            throw new ValidationException("Void reason is mandatory.");

        return await _settlementRepository.VoidPaymentAllocationAsync(allocationId, voidReason.Trim(), cancellationToken);
    }
}

