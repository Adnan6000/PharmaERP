# PharmaERP Architecture Guide

## 1. System Overview & Dependency Rules

PharmaERP is designed following Clean Architecture boundaries. Dependencies point strictly inwards:

```
PharmaERP.Desktop ──► PharmaERP.Application ──► PharmaERP.Domain
        │                       ▲
        ▼                       │
PharmaERP.Infrastructure ───────┘
```

### Invariant Rules
1. **Domain**: Must not reference Application, Infrastructure, or Desktop. No database annotations, no EF Core attributes.
2. **Application**: References only Domain. Contains business rules, repository contracts, and pagination models. Zero EF Core or SQL leaks (`IQueryable`, `AsNoTracking`, `DbSet`, etc. are prohibited).
3. **Infrastructure**: References Application and Domain. Houses EF Core 10 mappings, `AppDbContext`, pooled factory lifecycle, and SQL Server specifics.
4. **Desktop**: References Application and Infrastructure. WPF MVVM presentation shell, DI configuration, and resource dictionaries. Prohibited from executing SQL or referencing EF Core directly.

---

## 2. Dependency Injection & Service Lifetimes

WPF desktop applications have a single root `IServiceProvider`. To prevent memory leaks and state retention bugs:

| Layer | Service / Contract | Implementation | Lifetime | Rationale |
| :--- | :--- | :--- | :--- | :--- |
| **Application** | `IProductService` | `ProductService` | `Transient` | Stateless domain application service |
| **Infrastructure** | `IDbContextFactory<AppDbContext>` | Pooled Context Factory | `Singleton` (Factory) | Produces short-lived, pooled `AppDbContext` instances on-demand |
| **Infrastructure** | `IProductRepository` | `ProductRepository` | `Transient` | Creates and disposes DbContext per operation via factory |
| **Infrastructure** | `IDbConnectionTester` | `DbConnectionTester` | `Transient` | Asynchronous, non-blocking connectivity checks |
| **Desktop** | `MainWindowViewModel` | `MainWindowViewModel` | `Transient` | Bound to Window; avoids retaining stale UI state |
| **Desktop** | `MainWindow` | `MainWindow` | `Transient` | Resolved once at startup through DI host |

---

## 3. Database Lifecycle & Concurrency Strategy

### Pooled Context Factory (`IDbContextFactory<AppDbContext>`)
- A single long-lived `DbContext` must **never** be shared across a desktop application.
- All repositories inject `IDbContextFactory<AppDbContext>`.
- Methods use `await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);` ensuring immediate cleanup upon method completion.
- Read operations explicitly specify `.AsNoTracking()` to prevent tracking cache overhead and memory bloat.

### Multi-User Optimistic Concurrency (`RowVersion`)
- All persistent entities derive from `BaseEntity`.
- `BaseEntity` defines a framework-neutral `byte[] RowVersion { get; set; } = [];`.
- In `PharmaERP.Infrastructure`, `BaseEntityConfiguration<T>` maps `builder.Property(e => e.RowVersion).IsRowVersion();`.
- When multiple LAN clients edit the same record concurrently, EF Core raises a `DbUpdateConcurrencyException`, protecting records against silent overwrite.

### Controlled Migrations & Concurrency Gate
- Workstations do **not** run destructive schema migrations on standard application startup.
- In multi-client LAN setups, simultaneous migrations lead to race conditions, schema locks, and corruption.
- When migrations are executed via the First-Run Wizard or deployment scripts, `DatabaseMigrator` executes an explicit distributed application lock on SQL Server:
  - `sp_getapplock @Resource = 'PharmaERP_DatabaseMigration', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 30000`
  - Only the lock owner executes `context.Database.MigrateAsync()`, Chart of Accounts seeding, and operational gate activation.
  - Upon completion or failure, the lock is released via `sp_releaseapplock @Resource = 'PharmaERP_DatabaseMigration', @LockOwner = 'Session'`.
  - Concurrent clients waiting for the lock acquire it after the primary finishes and immediately recognize the database as up-to-date.

---

## 4. First-Run Setup & Bootstrap Architecture

### Clean Architecture Boundaries
- **PharmaERP.Application**: Defines neutral domain models and repository/store contracts:
  - `DatabaseConnectionConfig`: POCO representation of server name, database name, authentication mode, timeout, etc. Completely free of `SqlConnectionStringBuilder` or SQL client references.
  - `IDatabaseConfigStore`: Contract for loading and saving connection configurations.
  - `IConnectionStringFactory`: Contract for constructing connection strings from config models.
  - `ICredentialProtector`: Contract for encrypting and decrypting sensitive credentials.
- **PharmaERP.Infrastructure**: Implements database and OS-specific infrastructure:
  - `SqlConnectionStringFactory`: Uses `SqlConnectionStringBuilder` to construct safe, valid connection strings without manual string concatenation.
  - `WorkstationDatabaseConfigStore`: Manages `%LocalAppData%\PharmaERP\settings.json`, handling both canonical and legacy config schemas.
  - `DpapiCredentialProtector`: Encrypts/decrypts SQL passwords using Windows Data Protection API (`ProtectedData`, `DataProtectionScope.CurrentUser`).
  - `DbConnectionTester`: Tests reachability of `master` server, database existence, and `CREATE ANY DATABASE` server permissions with detailed diagnostics.
- **PharmaERP.Desktop**:
  - `DatabaseSetupWindow` / `DatabaseSetupViewModel`: Interactive first-run setup wizard allowing instance discovery, connection testing, database creation, migration execution, and profile saving.
  - `App.xaml.cs` Bootstrap Preflight: Executes before instantiating the heavy Microsoft DI Host or resolving data-dependent ViewModels (`MainWindowViewModel`, etc.). If configuration is absent or invalid, `DatabaseSetupWindow` is displayed modally.

---

## 5. Multi-PC LAN Deployment Topology

```
┌─────────────────────────────────┐
│       Central Server            │
│  SQL Server / SQL Server Express│
│  Port 1433 / Dedicated Instance │
└────────────────┬────────────────┘
                 │
       Office LAN (Ethernet/WiFi)
                 │
   ┌─────────────┼─────────────┐
   ▼             ▼             ▼
┌─────────────┐┌─────────────┐┌─────────────┐
│Workstation 1││Workstation 2││Workstation 3│
│PharmaERP App││PharmaERP App││PharmaERP App│
└─────────────┘└─────────────┘└─────────────┘
```

- **Network Prerequisites**:
  - Server TCP/IP protocol enabled in SQL Server Configuration Manager.
  - Port options:
    - **Static TCP Port (Recommended)**: Set *IPAll -> TCP Port* to a fixed port (e.g. 1433) and leave *TCP Dynamic Ports* blank. Allow inbound TCP on this port in Windows Firewall.
    - **Dynamic TCP Port + SQL Browser**: If SQL Server Express uses dynamic ports, allow incoming UDP on port 1434 for the SQL Server Browser service, and allow inbound connections for the `sqlservr.exe` binary in Windows Firewall.
- **Workstation Connection**:
  - Workstations configure LAN Server target (`SERVER-PC\SQLEXPRESS` or `192.168.1.100,1433`).
  - Database connectivity checks execute asynchronously via `IDbConnectionTester` and never freeze the WPF UI thread.

---

## 6. Security & Authentication Architecture

- **Windows Integrated Authentication**: Recommended for domain and single-PC networks (`Integrated Security=True`). Zero stored credentials.
- **SQL Authentication & DPAPI**: When SQL user accounts are required (e.g., non-domain workgroup LAN), credentials are encrypted locally on the workstation using **Windows DPAPI** (`DataProtectionScope.CurrentUser`). Plaintext passwords are never persisted to disk or output in log files.
- **Credential Masking**: All connection test logs and diagnostic messages automatically mask passwords (`Password=***`).
- **Runtime Precedence**:
  1. `PHARMAERP_CONNECTIONSTRING` environment variable (highest priority; for automated CI/CD and containers).
  2. `%LocalAppData%\PharmaERP\settings.json` (canonical per-workstation configuration).
  3. `appsettings.json` (fallback active profile).
- **Design-Time EF Precedence (`AppDbContextFactory`)**:
  1. `PHARMAERP_CONNECTIONSTRING` environment variable.
  2. Explicit local developer fallback: `Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True;`.
  *(Note: Design-time EF tooling intentionally does not query `%LocalAppData%\PharmaERP\settings.json` to prevent accidental schema changes to client/production databases).*

---

## 7. Financial & Packaging Modeling Precision

- **Monetary Precision**: Catalog prices (`DefaultPurchasePrice`, `DefaultSalePrice`) are mapped with `decimal(18,4)` precision via EF Core Fluent API.
- **Catalog vs Transaction Separation**: Default prices on `Product` are catalog defaults. Actual purchase and selling rates will belong to invoice line items and inventory batches in future milestones.
- **Packaging Hierarchy**: The `Unit` entity represents packaging units (Box, Strip, Tablet). Multi-level conversion factors will be added as a dedicated conversion table in Milestone 2.


---

## 8. Financial Accounting, Immutability & Operational Gate Architecture

### Immutable General Ledger & Two-Phase Post
- All accounting entries post through `AccountingTransactionWriter` via `PostTwoPhaseJournalAsync`.
- Journal headers and lines are permanently immutable in SQL Server once committed (`Status == Posted`).
- Any updates, deletions, or tampered narration transitions (value-to-value, value-to-null, null-to-value) are rejected at the database engine level via SQL Server DML triggers (`TR_JournalEntries_PreventPostedHeaderModifications` and `TR_JournalEntryLines_PreventPostedModifications`).
- Reversals and cancellations never mutate historical records; they atomically append a new compensating reversal journal entry marked with `PostingRole = JournalPostingRole.Reversal` and reference `ReversesJournalEntryId`.

### Control Account Integrity
- Subledger control accounts (`AccountsReceivableControl`, `AccountsPayableControl`, `Inventory`, `CostOfGoodsSold`) can only be affected by valid operational transactions or system vouchers (`SourceDocumentType` in Sale, Purchase, Return, Voucher).
- Manual journal entries (`SourceDocumentType = Manual`) attempting to touch control accounts are blocked by `TR_JournalEntryLines_PreventManualControlAccountPosting`.

### Concurrency-Safe Invoice Settlement & Allocation
- Payment settlements allocate receipt and payment vouchers against open invoices (`ReceiptVoucherAllocation`, `PaymentVoucherAllocation`).
- Allocation transactions execute with `IsolationLevel.Serializable` and row-level locks, computing returns-deducted effective net balances:
  `OutstandingBalance = (NetTotal - ReturnsTotal) - AllocatedTotal`
- Over-allocation beyond invoice outstanding balance or voucher remaining balance is guarded both in application validation and SQL check constraints.

### Allocation Void Lifecycle & Audit Trail
- Historical settlement allocation records are **never physically deleted** to maintain a complete, tamper-evident audit log.
- Each allocation entity (`ReceiptVoucherAllocation`, `PaymentVoucherAllocation`) maintains an audit lifecycle:
  - `Status`: `AllocationStatus` (`Active = 1`, `Voided = 2`).
  - `VoidedAtUtc`: UTC timestamp recorded upon voiding.
  - `VoidReason`: Mandatory, non-empty explanation entered by the user.
- Outstanding invoice calculations and remaining voucher balances count **ACTIVE allocations only**.
- Voiding an allocation generates **no General Ledger journal entries** because the original allocation was a settlement link that generated no GL entries (all double-entry balance movements occurred during voucher and invoice posting).
- **Cancellation Guards**: Vouchers and invoices cannot be cancelled while active allocations exist (`Status == AllocationStatus.Active`). Once all active allocations are voided with documented audit reasons, cancellation of the voucher or invoice can safely proceed.

### Purchase Module Credit-Only Domain Design
- All purchases in PharmaERP are on-account credit purchases by domain design.
- **Cash purchases do not exist in the Purchase module**: Every posted `PurchaseInvoice` credits `AccountsPayableControl` against the specified `SupplierId` and debits `Inventory`.
- Cash disbursements to suppliers are strictly handled via **Payment Vouchers (`PV`)** crediting Cash/Bank and debiting `AccountsPayableControl`.
- The settlement layer reconciles disbursements against vendor invoices by allocating Payment Vouchers to Purchase Invoices via `PaymentVoucherAllocation`.

### Reader-Writer Operational Gate (`IAccountingOperationalGate`)
- Daily sales, purchases, returns, and vouchers acquire a shared asynchronous gate lock (`AcquireSharedOperationalGateAsync`).
- System initialization and backfill processes acquire an exclusive gate lock (`AcquireExclusiveInitializationGateAsync`), draining running operational transactions before backfilling historical ledgers to guarantee zero race conditions.

