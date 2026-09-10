# PharmaERP Project Status & Milestone Roadmap

## Current Status: Milestone 5 (Financial Accounting & Ledgers) — COMPLETED

The foundational architecture has been successfully established and verified:

### Deliverables Completed
- [x] **Project Structure & References**: Clean dependency graph adhering to Clean Architecture. Zero SQL/EF references in Desktop, zero UI references in Domain/Application/Infrastructure.
- [x] **Domain Foundation**:
  - `BaseEntity` with `Id`, `CreatedAtUtc`, `UpdatedAtUtc`, and framework-neutral `RowVersion`.
  - `Product` entity with `DefaultPurchasePrice`, `DefaultSalePrice`, active status, and relationships to `Manufacturer`, `Category`, and `Unit`.
  - Strictly verified: **no `BatchNumber` or `ExpiryDate` on `Product`**.
  - `Manufacturer`, `Category`, `Unit` foundational entities.
- [x] **Application Layer**:
  - Zero EF Core leakage.
  - `IProductRepository` focused contract.
  - `IProductService` & `ProductService` with validation, pagination, and async execution.
  - `PaginationQuery` and `PagedResult<T>` server-side paging models.
  - `IDbConnectionTester` contract.
  - Microsoft DI configuration with `Transient` lifetimes.
- [x] **Infrastructure Layer**:
  - EF Core 10 with SQL Server provider.
  - `AppDbContext` with Fluent API mappings and `BaseEntity` ignored from direct table inheritance.
  - `IDbContextFactory<AppDbContext>` pooled factory configuration.
  - `decimal(18,4)` precision for monetary values.
  - Targeted non-clustered indexes on `ProductCode`, `Barcode`, `Name`.
  - `ProductRepository` implementation with short-lived contexts, `.AsNoTracking()`, and server-side paging.
  - Non-blocking `DbConnectionTester`.
- [x] **Desktop Presentation**:
  - Microsoft DI Host integration in `App.xaml.cs`.
  - Safe `appsettings.json` supporting `Development`, `Local`, and `LAN` database profiles.
  - Pure WPF resource dictionaries (`Brushes.xaml`, `Typography.xaml`, `Styles.xaml`).
  - Standardized hover, focus visual, disabled states, and UI virtualization readiness.
  - Lightweight `MainWindow` shell with title, profile indicator, async health checker, and navigation placeholders.
- [x] **Unit Testing**:
  - `tests/PharmaERP.Domain.Tests`: Entity auditing, decimal pricing, and batch-omission verification tests.
  - `tests/PharmaERP.Application.Tests`: Service validation, business constraints, and pagination tests.
  - 100% pass rate (7/7 tests passing).
- [x] **Build Status**: Solution builds with **0 errors and 0 warnings**.

---

## Future Milestone Roadmap

### Milestone 2: Product Catalog & Master Data Management — COMPLETED
- [x] Full CRUD operations for Products, Manufacturers, Categories, and Units.
- [x] Packaging unit modeling and display abbreviation.
- [x] Virtualized product catalog list with server-side pagination and instantaneous search filtering.
- [x] Barcode and product code duplicate validation.

### Milestone 3: Batches, Expiry, Purchases, Returns & Stock Movements — COMPLETED
- [x] Independent `ProductBatch` entity tracking `BatchNumber`, `ExpiryDate`, `QuantityOnHand`, `AveragePurchaseCost`, `InventoryValue`.
- [x] Purchase Invoices (`PINV-YYYY-XXXXXX`) and Purchase Returns (`PRET-YYYY-XXXXXX`).
- [x] Atomic SQL stock mutations with `OUTPUT` capturing exact cost rate and value changes.
- [x] Concurrency-safe document numbering using `sp_getapplock`.
- [x] Downstream consumption and active return guards on cancellation.

### Milestone 4: Sales POS, Multi-Batch FEFO, Sales Returns & Invoice Printing — COMPLETED
- [x] Fast keyboard-centric Point of Sale (POS) interface (`SalesEntryView`).
- [x] Barcode scanner, product code, and partial name/generic lookup with live stock aggregation (`ProductLookupService`).
- [x] In-transaction deterministic FEFO allocation (`ExpiryDate ASC, Id ASC`) with strict expired-batch exclusion.
- [x] Authoritative COGS and inventory valuation capture via SQL `OUTPUT` delta (`OldInventoryValue - NewInventoryValue`).
- [x] Proportional line and invoice discount allocation and refund computation.
- [x] Exact original-batch and valuation restoration on sales returns with DB-level cumulative return guards (`WHERE ReturnedQuantity + @qty <= Quantity`).
- [x] Sale and Sale Return cancellation with downstream consumption safeguards.
- [x] Document numbering for Sales: `SINV-YYYY-XXXXXX` and `SRET-YYYY-XXXXXX` (e.g. `SINV-2026-000001`, `SRET-2026-000001`).
- [x] Enums:
  - `SaleType`: `Cash = 1`, `Credit = 2`
  - `SaleInvoiceStatus`: `Draft = 1`, `Posted = 2`, `Cancelled = 3`
  - `SaleReturnStatus`: `Draft = 1`, `Posted = 2`, `Cancelled = 3`
- [x] Dual layout printing engine: High-speed 80mm continuous thermal receipts and formal A4 invoices with immutable print identity snapshots.
- [x] Unified print preview and reprint dialog from Sales History.
- [x] Authoritative Keyboard Shortcuts:
  - `F2`: Purchase Entry
  - `F3`: POS / Sales Counter
  - `F5`: Refresh
  - `Ctrl+N`: New / Clear POS Ticket
  - `Ctrl+F`: Focus Barcode / Product Search Box
  - `Enter` (in Search): Lookup & Add to Cart
  - `F8`: Park Current Ticket
  - `F9`: Recall Parked Ticket
  - `F10`: Open Tender / Payment Modal
  - `Enter` (in Tender): Post Sale & Trigger Receipt
  - `F12`: Quick Cash Sale (instant single-key checkout)
  - `Delete`: Remove selected cart item
  - `Escape`: Close modal / clear active form

---

## Future Milestone Roadmap

### Milestone 5: Financial Accounting, Ledgers & Payment Settlement — COMPLETED
- [x] **Chart of Accounts & Hierarchical Ledger**:
  - Full account classification: Assets (1xxx), Liabilities (2xxx), Equity (3xxx), Revenue (4xxx), Expenses (5xxx).
  - Parent-child tree structure, active status, system account tags (`SystemAccountType`), and posting controls (`AllowPosting`).
  - Control accounts for AR (1030), AP (2010), Inventory (1040), COGS (5010).
- [x] **Strict Double-Entry Database Integrity**:
  - Database trigger `TR_JournalEntries_PreventImbalance` enforcing exact `TotalDebit == TotalCredit` before insert/update.
  - Database trigger `TR_JournalEntries_PreventPostedHeaderModifications` enforcing total immutability of posted journal headers (including strict `Narration` immutability for all transitions: value->value, value->null, null->value).
  - Database trigger `TR_JournalEntryLines_PreventPostedModifications` rejecting any UPDATE, INSERT, or DELETE on posted journal lines.
  - Database trigger `TR_JournalEntryLines_PreventManualControlAccountPosting` strictly disallowing manual journal postings directly to AR, AP, Inventory, or COGS control accounts.
- [x] **First-Class Double-Entry Vouchers**:
  - Receipt Voucher (`RV-YYYY-XXXXXX`): Customer collections into cash/bank with automatic debit to Cash/Bank and credit to AR Control.
  - Payment Voucher (`PV-YYYY-XXXXXX`): Supplier vendor disbursements with automatic debit to AP Control and credit to Cash/Bank.
  - Contra Voucher (`CV-YYYY-XXXXXX`): Cash <-> Bank and Bank <-> Bank transfers with strict account type validations and same-account transfer blocks.
  - Journal Voucher (`JV-YYYY-XXXXXX`): Multi-line manual journal adjustments with live balance validation.
  - Opening Balance Voucher (`OB-YYYY-XXXXXX`): Initial chart of accounts setup with Opening Balance Equity balancing.
  - Immutable voucher cancellation posting atomic compensating reversal journal entries (`PostingRole = JournalPostingRole.Reversal`).
- [x] **Invoice-Level Payment Reconciliation & Settlement Allocation**:
  - `ReceiptVoucherAllocation` and `PaymentVoucherAllocation` tracking exact allocations per invoice.
  - Automatic invoice status calculation: `Unpaid`, `PartiallyPaid`, `Paid`.
  - Effective outstanding balance computation deducting posted sales returns and purchase returns.
  - Concurrency-safe serializable locks preventing over-allocation and remaining voucher balance over-allocation.
  - **Settlement Unallocate / Void Lifecycle & Audit Trail**:
    - Audit-safe allocation lifecycle: `AllocationStatus` (`Active = 1`, `Voided = 2`), `VoidedAtUtc`, and mandatory `VoidReason`.
    - Historical allocation records are **never physically deleted**.
    - Outstanding invoice balances and remaining voucher balances count **ACTIVE allocations only**.
    - Authorized voiding exposed in Service, Repository, and Desktop UI with mandatory reason input.
    - Voiding generates **no GL journal entries** (reversing settlement links without touching GL balances).
    - Voucher and invoice cancellation guards allow cancellation to proceed once all active allocations are voided.
- [x] **Purchase Module Credit-Only Domain Design**:
  - Confirmed and verified architectural source of truth: the Purchase module supports **on-account credit purchases only** (Dr Inventory / Cr AccountsPayableControl against `SupplierId`).
  - Cash purchases do NOT exist in the Purchase module. Supplier disbursements are strictly modeled as Payment Vouchers (PV) crediting Cash/Bank and debiting AP Control, followed by invoice settlement allocation.
- [x] **Financial Statements & Ledgers**:
  - **General Ledger**: Filtered double-entry audit trail with pagination and running balance.
  - **Dedicated Cash Book & Bank Book**: Dedicated views for cash on hand and bank accounts with inflow/outflow separation.
  - **Customer & Supplier Subledgers**: Transaction history showing charges, payments, and running balance per party.
  - **Day Book**: Chronological multi-line daily journal activity log.
  - **Trial Balance**: Real-time summary of all debit and credit balances with mathematical balance verification indicator.
  - **Profit & Loss Statement**: Income statement showing Operating Revenue, Cost of Goods Sold, Operating Expenses, and Net Profit/Loss margin.
  - **Balance Sheet**: Point-in-time financial statement showing Assets, Liabilities, Equity, and explicit Current Period Earnings balancing Assets = Liabilities + Equity.
- [x] **Historical Initialization & 4-Way Automated Reconciliation**:
  - Reader-Writer operational gate (`IAccountingOperationalGate`) allowing non-blocking concurrent operations while providing mutual exclusion during initialization.
  - Historical backfill runner transforming legacy operational records (opening stocks, purchases, purchase returns, sales, sales returns) into double-entry journals.
  - Automated 4-Way Reconciliation verifying:
    1. Inventory GL Balance == Physical Stock Valuation.
    2. AR Control Balance == Customer Subledger Totals.
    3. AP Control Balance == Supplier Subledger Totals.
    4. Trial Balance Total Debits == Total Credits.
- [x] **Desktop Presentation**:
  - Chart of Accounts management (`ChartOfAccountsView`).
  - Vouchers management (`VouchersView`) with drawers for RV, PV, CV, JV, OB, cancellation, and invoice settlement allocation drawer featuring live outstanding invoices, active/voided status, and authorized allocation voiding.
  - Financial reports and ledgers (`LedgersView`) with 9 dedicated tabs: General Ledger, Cash Book, Bank Book, Customer Ledger, Supplier Ledger, Day Book, Trial Balance, Profit & Loss, Balance Sheet.
  - System accounting configuration & reconciliation dashboard (`AccountingSetupView`).
- [x] **Hardened Financial & Reconciliation Rules**:
  - Settlement Party Integrity: Mandatory non-null CustomerId/SupplierId matching between vouchers and invoices. General vouchers and cross-party allocations strictly rejected.
  - Credit-Only Settlements: Cash invoices excluded from open invoices and rejected on allocation write path.
  - Lifecycle Integrity Guards: Voucher and Invoice cancellation strictly blocked while active settlement allocations exist, but cleanly unblocked after voiding.
  - Return After Payment: Over-settlement from post-settlement returns clamped to non-negative zero.
  - Concurrency Safety: `IsolationLevel.Serializable` key-range locking verified with 4 concurrent test suites preventing over-allocation.
  - Financial Report Integrity: P&L and Balance Sheet rigorously verify child account aggregation, non-double-counting, draft journal exclusion, and date boundaries.
- [x] **Automated Verification**:
  - 95 passing tests across Domain (3), Application (27), and Infrastructure (65) test suites against real SQL Server (`.\SQLEXPRESS`) with 0 warnings, 0 errors, 0 failed, 0 skipped.
