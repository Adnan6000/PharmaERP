using System.Data;
using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class SettlementRepository : ISettlementRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SettlementRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ReceiptVoucherAllocationDto> AllocateReceiptAsync(ReceiptAllocationCreateDto dto, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var voucher = await context.ReceiptVouchers
                .FirstOrDefaultAsync(v => v.Id == dto.ReceiptVoucherId, cancellationToken)
                ?? throw new NotFoundException($"Receipt voucher with ID {dto.ReceiptVoucherId} was not found.");

            if (voucher.Status != VoucherStatus.Posted)
                throw new ValidationException($"Cannot allocate from receipt voucher '{voucher.VoucherNumber}' because its status is {voucher.Status}.");

            var invoice = await context.SaleInvoices
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == dto.SaleInvoiceId, cancellationToken)
                ?? throw new NotFoundException($"Sale invoice with ID {dto.SaleInvoiceId} was not found.");

            if (invoice.Status != SaleInvoiceStatus.Posted)
                throw new ValidationException($"Cannot allocate to sale invoice '{invoice.InvoiceNumber}' because its status is {invoice.Status}.");

            if (!voucher.CustomerId.HasValue)
                throw new ValidationException("Cannot allocate from a general receipt voucher not assigned to a customer.");

            if (!invoice.CustomerId.HasValue)
                throw new ValidationException("Cannot allocate to a sale invoice with no customer assigned.");

            if (voucher.CustomerId.Value != invoice.CustomerId.Value)
                throw new ValidationException($"Receipt voucher customer (ID {voucher.CustomerId.Value}) does not match sale invoice customer (ID {invoice.CustomerId.Value}).");

            if (invoice.SaleType != SaleType.Credit)
                throw new ValidationException($"Cannot allocate to sale invoice '{invoice.InvoiceNumber}' because it is a {invoice.SaleType} sale. Only credit sale invoices can be settled via receipt voucher allocation.");

            // Calculate voucher unallocated balance
            decimal voucherAllocated = await context.ReceiptVoucherAllocations
                .Where(a => a.ReceiptVoucherId == voucher.Id && a.Status == AllocationStatus.Active)
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            decimal voucherRemaining = voucher.Amount - voucherAllocated;
            if (dto.AllocatedAmount > voucherRemaining)
                throw new ValidationException($"Allocated amount ({dto.AllocatedAmount:N2}) exceeds unallocated voucher balance ({voucherRemaining:N2}).");

            // Calculate invoice outstanding balance
            decimal invoiceReturns = await context.SaleReturns
                .Where(r => r.OriginalSaleInvoiceId == invoice.Id && r.Status == SaleReturnStatus.Posted)
                .SumAsync(r => (decimal?)r.TotalAmount, cancellationToken) ?? 0m;

            decimal invoiceAllocated = await context.ReceiptVoucherAllocations
                .Where(a => a.SaleInvoiceId == invoice.Id && a.Status == AllocationStatus.Active)
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            decimal invoiceOutstanding = invoice.NetTotal - invoiceReturns - invoiceAllocated;
            if (dto.AllocatedAmount > invoiceOutstanding)
                throw new ValidationException($"Allocated amount ({dto.AllocatedAmount:N2}) exceeds invoice outstanding balance ({invoiceOutstanding:N2}).");

            var allocation = new ReceiptVoucherAllocation
            {
                ReceiptVoucherId = voucher.Id,
                SaleInvoiceId = invoice.Id,
                AllocatedAmount = dto.AllocatedAmount,
                AllocatedAtUtc = DateTime.UtcNow,
                Status = AllocationStatus.Active
            };

            await context.ReceiptVoucherAllocations.AddAsync(allocation, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ReceiptVoucherAllocationDto
            {
                Id = allocation.Id,
                ReceiptVoucherId = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                SaleInvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                AllocatedAtUtc = allocation.AllocatedAtUtc,
                Status = allocation.Status,
                VoidedAtUtc = allocation.VoidedAtUtc,
                VoidReason = allocation.VoidReason
            };

        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentVoucherAllocationDto> AllocatePaymentAsync(PaymentAllocationCreateDto dto, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var voucher = await context.PaymentVouchers
                .FirstOrDefaultAsync(v => v.Id == dto.PaymentVoucherId, cancellationToken)
                ?? throw new NotFoundException($"Payment voucher with ID {dto.PaymentVoucherId} was not found.");

            if (voucher.Status != VoucherStatus.Posted)
                throw new ValidationException($"Cannot allocate from payment voucher '{voucher.VoucherNumber}' because its status is {voucher.Status}.");

            var invoice = await context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.Id == dto.PurchaseInvoiceId, cancellationToken)
                ?? throw new NotFoundException($"Purchase invoice with ID {dto.PurchaseInvoiceId} was not found.");

            if (invoice.Status != PurchaseInvoiceStatus.Posted)
                throw new ValidationException($"Cannot allocate to purchase invoice '{invoice.InvoiceNumber}' because its status is {invoice.Status}.");

            if (!voucher.SupplierId.HasValue)
                throw new ValidationException("Cannot allocate from a general payment voucher not assigned to a supplier.");

            if (voucher.SupplierId.Value != invoice.SupplierId)
                throw new ValidationException($"Payment voucher supplier (ID {voucher.SupplierId.Value}) does not match purchase invoice supplier (ID {invoice.SupplierId}).");

            // Calculate voucher unallocated balance
            decimal voucherAllocated = await context.PaymentVoucherAllocations
                .Where(a => a.PaymentVoucherId == voucher.Id && a.Status == AllocationStatus.Active)
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            decimal voucherRemaining = voucher.Amount - voucherAllocated;
            if (dto.AllocatedAmount > voucherRemaining)
                throw new ValidationException($"Allocated amount ({dto.AllocatedAmount:N2}) exceeds unallocated voucher balance ({voucherRemaining:N2}).");

            // Calculate invoice outstanding balance
            decimal invoiceReturns = await context.PurchaseReturns
                .Where(r => r.OriginalPurchaseInvoiceId == invoice.Id && r.Status == PurchaseReturnStatus.Posted)
                .SumAsync(r => (decimal?)r.TotalAmount, cancellationToken) ?? 0m;

            decimal invoiceAllocated = await context.PaymentVoucherAllocations
                .Where(a => a.PurchaseInvoiceId == invoice.Id && a.Status == AllocationStatus.Active)
                .SumAsync(a => (decimal?)a.AllocatedAmount, cancellationToken) ?? 0m;

            decimal invoiceOutstanding = invoice.NetTotal - invoiceReturns - invoiceAllocated;
            if (dto.AllocatedAmount > invoiceOutstanding)
                throw new ValidationException($"Allocated amount ({dto.AllocatedAmount:N2}) exceeds invoice outstanding balance ({invoiceOutstanding:N2}).");

            var allocation = new PaymentVoucherAllocation
            {
                PaymentVoucherId = voucher.Id,
                PurchaseInvoiceId = invoice.Id,
                AllocatedAmount = dto.AllocatedAmount,
                AllocatedAtUtc = DateTime.UtcNow,
                Status = AllocationStatus.Active
            };

            await context.PaymentVoucherAllocations.AddAsync(allocation, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PaymentVoucherAllocationDto
            {
                Id = allocation.Id,
                PaymentVoucherId = voucher.Id,
                VoucherNumber = voucher.VoucherNumber,
                PurchaseInvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                AllocatedAtUtc = allocation.AllocatedAtUtc,
                Status = allocation.Status,
                VoidedAtUtc = allocation.VoidedAtUtc,
                VoidReason = allocation.VoidReason
            };

        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<List<OpenSaleInvoiceDto>> GetCustomerOpenInvoicesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var invoices = await context.SaleInvoices
            .Include(s => s.Customer)
            .Where(s => s.CustomerId == customerId && s.Status == SaleInvoiceStatus.Posted && s.SaleType == SaleType.Credit)
            .OrderBy(s => s.BusinessDate)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var invoiceIds = invoices.Select(i => i.Id).ToList();

        var returnsByInvoice = await context.SaleReturns
            .Where(r => invoiceIds.Contains(r.OriginalSaleInvoiceId) && r.Status == SaleReturnStatus.Posted)
            .GroupBy(r => r.OriginalSaleInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Returned = g.Sum(r => r.TotalAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Returned, cancellationToken);

        var allocationsByInvoice = await context.ReceiptVoucherAllocations
            .Where(a => invoiceIds.Contains(a.SaleInvoiceId) && a.Status == AllocationStatus.Active)
            .GroupBy(a => a.SaleInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Allocated = g.Sum(a => a.AllocatedAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Allocated, cancellationToken);

        var result = new List<OpenSaleInvoiceDto>();
        foreach (var inv in invoices)
        {
            returnsByInvoice.TryGetValue(inv.Id, out decimal ret);
            allocationsByInvoice.TryGetValue(inv.Id, out decimal alloc);

            decimal effectiveNet = Math.Max(0m, inv.NetTotal - ret);
            decimal outstanding = Math.Max(0m, effectiveNet - alloc);

            if (outstanding > 0m)
            {
                result.Add(new OpenSaleInvoiceDto
                {
                    InvoiceId = inv.Id,
                    InvoiceNumber = inv.InvoiceNumber,
                    BusinessDate = inv.BusinessDate,
                    CustomerId = inv.CustomerId,
                    CustomerName = inv.Customer?.Name ?? inv.CustomerNameSnapshot,
                    NetTotal = effectiveNet,
                    TotalAllocated = alloc
                });
            }
        }

        return result;
    }

    public async Task<List<OpenPurchaseInvoiceDto>> GetSupplierOpenInvoicesAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var invoices = await context.PurchaseInvoices
            .Include(p => p.Supplier)
            .Where(p => p.SupplierId == supplierId && p.Status == PurchaseInvoiceStatus.Posted)
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var invoiceIds = invoices.Select(i => i.Id).ToList();

        var returnsByInvoice = await context.PurchaseReturns
            .Where(r => r.OriginalPurchaseInvoiceId.HasValue && invoiceIds.Contains(r.OriginalPurchaseInvoiceId.Value) && r.Status == PurchaseReturnStatus.Posted)
            .GroupBy(r => r.OriginalPurchaseInvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Returned = g.Sum(r => r.TotalAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Returned, cancellationToken);

        var allocationsByInvoice = await context.PaymentVoucherAllocations
            .Where(a => invoiceIds.Contains(a.PurchaseInvoiceId) && a.Status == AllocationStatus.Active)
            .GroupBy(a => a.PurchaseInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Allocated = g.Sum(a => a.AllocatedAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Allocated, cancellationToken);

        var result = new List<OpenPurchaseInvoiceDto>();
        foreach (var inv in invoices)
        {
            returnsByInvoice.TryGetValue(inv.Id, out decimal ret);
            allocationsByInvoice.TryGetValue(inv.Id, out decimal alloc);

            decimal effectiveNet = Math.Max(0m, inv.NetTotal - ret);
            decimal outstanding = Math.Max(0m, effectiveNet - alloc);

            if (outstanding > 0m)
            {
                result.Add(new OpenPurchaseInvoiceDto
                {
                    InvoiceId = inv.Id,
                    InvoiceNumber = inv.InvoiceNumber,
                    SupplierInvoiceNumber = inv.SupplierInvoiceNumber,
                    InvoiceDate = inv.InvoiceDate,
                    SupplierId = inv.SupplierId,
                    SupplierName = inv.Supplier?.Name ?? string.Empty,
                    NetTotal = effectiveNet,
                    TotalAllocated = alloc
                });
            }
        }

        return result;
    }

    public async Task<List<ReceiptVoucherAllocationDto>> GetReceiptVoucherAllocationsAsync(int receiptVoucherId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ReceiptVoucherAllocations
            .Include(a => a.ReceiptVoucher)
            .Include(a => a.SaleInvoice)
            .Where(a => a.ReceiptVoucherId == receiptVoucherId)
            .OrderBy(a => a.AllocatedAtUtc)
            .Select(a => new ReceiptVoucherAllocationDto
            {
                Id = a.Id,
                ReceiptVoucherId = a.ReceiptVoucherId,
                VoucherNumber = a.ReceiptVoucher.VoucherNumber,
                SaleInvoiceId = a.SaleInvoiceId,
                InvoiceNumber = a.SaleInvoice.InvoiceNumber,
                AllocatedAmount = a.AllocatedAmount,
                AllocatedAtUtc = a.AllocatedAtUtc,
                Status = a.Status,
                VoidedAtUtc = a.VoidedAtUtc,
                VoidReason = a.VoidReason
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PaymentVoucherAllocationDto>> GetPaymentVoucherAllocationsAsync(int paymentVoucherId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PaymentVoucherAllocations
            .Include(a => a.PaymentVoucher)
            .Include(a => a.PurchaseInvoice)
            .Where(a => a.PaymentVoucherId == paymentVoucherId)
            .OrderBy(a => a.AllocatedAtUtc)
            .Select(a => new PaymentVoucherAllocationDto
            {
                Id = a.Id,
                PaymentVoucherId = a.PaymentVoucherId,
                VoucherNumber = a.PaymentVoucher.VoucherNumber,
                PurchaseInvoiceId = a.PurchaseInvoiceId,
                InvoiceNumber = a.PurchaseInvoice.InvoiceNumber,
                AllocatedAmount = a.AllocatedAmount,
                AllocatedAtUtc = a.AllocatedAtUtc,
                Status = a.Status,
                VoidedAtUtc = a.VoidedAtUtc,
                VoidReason = a.VoidReason
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ReceiptVoucherAllocationDto> VoidReceiptAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(voidReason))
            throw new ValidationException("Void reason is mandatory.");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var allocation = await context.ReceiptVoucherAllocations
                .Include(a => a.ReceiptVoucher)
                .Include(a => a.SaleInvoice)
                .FirstOrDefaultAsync(a => a.Id == allocationId, cancellationToken)
                ?? throw new NotFoundException($"Receipt voucher allocation with ID {allocationId} was not found.");

            if (allocation.Status == AllocationStatus.Voided)
                throw new ValidationException($"Allocation {allocationId} is already voided.");

            allocation.Status = AllocationStatus.Voided;
            allocation.VoidedAtUtc = DateTime.UtcNow;
            allocation.VoidReason = voidReason.Trim();

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ReceiptVoucherAllocationDto
            {
                Id = allocation.Id,
                ReceiptVoucherId = allocation.ReceiptVoucherId,
                VoucherNumber = allocation.ReceiptVoucher.VoucherNumber,
                SaleInvoiceId = allocation.SaleInvoiceId,
                InvoiceNumber = allocation.SaleInvoice.InvoiceNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                AllocatedAtUtc = allocation.AllocatedAtUtc,
                Status = allocation.Status,
                VoidedAtUtc = allocation.VoidedAtUtc,
                VoidReason = allocation.VoidReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentVoucherAllocationDto> VoidPaymentAllocationAsync(int allocationId, string voidReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(voidReason))
            throw new ValidationException("Void reason is mandatory.");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var allocation = await context.PaymentVoucherAllocations
                .Include(a => a.PaymentVoucher)
                .Include(a => a.PurchaseInvoice)
                .FirstOrDefaultAsync(a => a.Id == allocationId, cancellationToken)
                ?? throw new NotFoundException($"Payment voucher allocation with ID {allocationId} was not found.");

            if (allocation.Status == AllocationStatus.Voided)
                throw new ValidationException($"Allocation {allocationId} is already voided.");

            allocation.Status = AllocationStatus.Voided;
            allocation.VoidedAtUtc = DateTime.UtcNow;
            allocation.VoidReason = voidReason.Trim();

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PaymentVoucherAllocationDto
            {
                Id = allocation.Id,
                PaymentVoucherId = allocation.PaymentVoucherId,
                VoucherNumber = allocation.PaymentVoucher.VoucherNumber,
                PurchaseInvoiceId = allocation.PurchaseInvoiceId,
                InvoiceNumber = allocation.PurchaseInvoice.InvoiceNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                AllocatedAtUtc = allocation.AllocatedAtUtc,
                Status = allocation.Status,
                VoidedAtUtc = allocation.VoidedAtUtc,
                VoidReason = allocation.VoidReason
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

