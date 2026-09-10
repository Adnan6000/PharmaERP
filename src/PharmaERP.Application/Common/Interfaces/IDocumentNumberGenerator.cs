namespace PharmaERP.Application.Common.Interfaces;

public enum DocumentType
{
    PurchaseInvoice = 1,
    PurchaseReturn = 2,
    Customer = 3,
    Supplier = 4,
    SaleInvoice = 5,
    SaleReturn = 6,
    JournalEntry = 7,
    ReceiptVoucher = 8,
    PaymentVoucher = 9,
    JournalVoucher = 10,
    OpeningBalanceVoucher = 11,
    ContraVoucher = 12
}

public interface IDocumentNumberGenerator
{
    Task<string> NextDocumentNumberAsync(DocumentType type, int? year = null, CancellationToken cancellationToken = default);
}

