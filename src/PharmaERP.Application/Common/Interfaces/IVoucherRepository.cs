using PharmaERP.Domain.Entities;

namespace PharmaERP.Application.Common.Interfaces;

public interface IVoucherRepository
{
    Task<ReceiptVoucher?> GetReceiptVoucherByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PaymentVoucher?> GetPaymentVoucherByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<JournalVoucher?> GetJournalVoucherByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<OpeningBalanceVoucher?> GetOpeningBalanceVoucherByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ContraVoucher?> GetContraVoucherByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<List<ReceiptVoucher>> GetReceiptVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<PaymentVoucher>> GetPaymentVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<JournalVoucher>> GetJournalVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<OpeningBalanceVoucher>> GetOpeningBalanceVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
    Task<List<ContraVoucher>> GetContraVouchersAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);
}
