# PharmaERP Project Status & Milestone Roadmap

## Current Status: Milestone 1 (Foundation) — COMPLETED

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

### Milestone 5: Financial Accounting & Ledgers
- Chart of accounts.
- Customer and Supplier credit ledgers.
- Cash book, bank book, and payment reconciliation.
- Double-entry accounting vouchers (Payment, Receipt, Journal, Contra).
- Financial reports: Daybook, Trial Balance, Profit & Loss, Balance Sheet.
