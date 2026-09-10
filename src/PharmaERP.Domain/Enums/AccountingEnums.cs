namespace PharmaERP.Domain.Enums;

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5
}

public enum SystemAccountType
{
    CashOnHand = 1,
    AccountsReceivableControl = 2,
    AccountsPayableControl = 3,
    Inventory = 4,
    SalesRevenue = 5,
    SalesDiscount = 6,
    SalesReturns = 7,
    CostOfGoodsSold = 8,
    PurchaseDiscount = 9,
    OpeningBalanceEquity = 10,
    DefaultBankAccount = 11
}

public enum JournalEntryStatus
{
    Draft = 1,
    Posted = 2
}

public enum JournalPostingRole
{
    Primary = 1,
    Reversal = 2
}

public enum JournalSourceDocumentType
{
    Manual = 1,
    SaleInvoice = 2,
    SaleReturn = 3,
    PurchaseInvoice = 4,
    PurchaseReturn = 5,
    ReceiptVoucher = 6,
    PaymentVoucher = 7,
    JournalVoucher = 8,
    OpeningStock = 9,
    OpeningBalance = 10,
    ContraVoucher = 11
}

public enum VoucherStatus
{
    Posted = 2,
    Cancelled = 3
}

public enum AccountingSetupState
{
    NotConfigured = 1,
    Initializing = 2,
    InitializationFailed = 3,
    Active = 4
}

public enum AllocationStatus
{
    Active = 1,
    Voided = 2
}

