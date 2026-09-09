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

### Controlled Migrations
- Workstations do **not** run EF Core migrations on application startup.
- Automatic workstation migrations in multi-client LAN setups lead to race conditions, locked schema states, and unauthorized schema alterations.
- Database migrations are designated strictly as a controlled administrative or deployment operation.

---

## 4. Multi-PC LAN Deployment Topology

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

- Workstations configure `"ActiveProfile": "LAN"` pointing to the server IP / hostname.
- Database connectivity checks execute in the background asynchronously via `IDbConnectionTester` and never freeze the WPF UI thread.

---

## 5. Security & Authentication Architecture

- **Windows Integrated Authentication**: Recommended for domain and office networks. No credentials stored in application files.
- **SQL Authentication**: When SQL user accounts are required, credentials must be retrieved from Windows DPAPI or Windows Credential Manager rather than stored in `appsettings.json`.
- **Environment Overrides**: `PHARMAERP_CONNECTIONSTRING` is supported for automated CI/CD and deployment environments.

---

## 6. Financial & Packaging Modeling Precision

- **Monetary Precision**: Catalog prices (`DefaultPurchasePrice`, `DefaultSalePrice`) are mapped with `decimal(18,4)` precision via EF Core Fluent API.
- **Catalog vs Transaction Separation**: Default prices on `Product` are catalog defaults. Actual purchase and selling rates will belong to invoice line items and inventory batches in future milestones.
- **Packaging Hierarchy**: The `Unit` entity represents packaging units (Box, Strip, Tablet). Multi-level conversion factors will be added as a dedicated conversion table in Milestone 2.

