using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Common.Interfaces;

public interface IAccountingTransactionWriter
{
    Task<ReceiptVoucherDto> CreateAndPostReceiptVoucherAsync(string voucherNumber, string journalEntryNumber, ReceiptVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<ReceiptVoucherDto> CancelReceiptVoucherAsync(int voucherId, string reversalEntryNumber, string cancellationReason, CancellationToken cancellationToken = default);

    Task<PaymentVoucherDto> CreateAndPostPaymentVoucherAsync(string voucherNumber, string journalEntryNumber, PaymentVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<PaymentVoucherDto> CancelPaymentVoucherAsync(int voucherId, string reversalEntryNumber, string cancellationReason, CancellationToken cancellationToken = default);

    Task<JournalVoucherDto> CreateAndPostJournalVoucherAsync(string voucherNumber, string journalEntryNumber, JournalVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<JournalVoucherDto> CancelJournalVoucherAsync(int voucherId, string reversalEntryNumber, string cancellationReason, CancellationToken cancellationToken = default);

    Task<OpeningBalanceVoucherDto> CreateAndPostOpeningBalanceVoucherAsync(string voucherNumber, string journalEntryNumber, OpeningBalanceVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<OpeningBalanceVoucherDto> CancelOpeningBalanceVoucherAsync(int voucherId, string reversalEntryNumber, string cancellationReason, CancellationToken cancellationToken = default);

    Task<ContraVoucherDto> CreateAndPostContraVoucherAsync(string voucherNumber, string journalEntryNumber, ContraVoucherCreateDto dto, CancellationToken cancellationToken = default);
    Task<ContraVoucherDto> CancelContraVoucherAsync(int voucherId, string reversalEntryNumber, string cancellationReason, CancellationToken cancellationToken = default);

    Task<JournalEntryDto> PostManualJournalAsync(JournalEntry entry, List<JournalEntryLine> lines, CancellationToken cancellationToken = default);

    // Operational integration hooks
    Task PostSaleInvoiceJournalAsync(object dbContext, SaleInvoice invoice, decimal totalCogsValue, CancellationToken cancellationToken = default);
    Task PostSaleInvoiceReversalAsync(object dbContext, SaleInvoice invoice, string reason, CancellationToken cancellationToken = default);
    Task PostSaleReturnJournalAsync(object dbContext, SaleReturn saleReturn, decimal totalRestoredInventoryValue, CancellationToken cancellationToken = default);
    Task PostSaleReturnReversalAsync(object dbContext, SaleReturn saleReturn, string reason, CancellationToken cancellationToken = default);

    Task PostPurchaseInvoiceJournalAsync(object dbContext, PurchaseInvoice invoice, CancellationToken cancellationToken = default);
    Task PostPurchaseInvoiceReversalAsync(object dbContext, PurchaseInvoice invoice, string reason, CancellationToken cancellationToken = default);
    Task PostPurchaseReturnJournalAsync(object dbContext, PurchaseReturn purchaseReturn, CancellationToken cancellationToken = default);
    Task PostPurchaseReturnReversalAsync(object dbContext, PurchaseReturn purchaseReturn, string reason, CancellationToken cancellationToken = default);

    Task PostOpeningStockJournalAsync(object dbContext, StockMovement movement, CancellationToken cancellationToken = default);
}
