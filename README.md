# PharmaERP

PharmaERP is a professional, high-performance Windows desktop Pharmaceutical ERP and Accounting application built on **C#**, **.NET 10**, **WPF**, **Entity Framework Core 10**, and **SQL Server**.

The system is designed for pharmacy operations running in either:
1. **Single-PC Mode**: Standalone installation using a local SQL Server Express instance (`.\SQLEXPRESS`).
2. **Multi-PC Office LAN Mode**: Multiple client workstations connected across a local area network to a central SQL Server database.

---

## Architectural Principles

The solution strictly adheres to Clean Architecture:
- **PharmaERP.Domain**: Core business entities and optimistic concurrency tokens. Zero dependencies on other projects, persistence, or UI.
- **PharmaERP.Application**: Application services, pagination models, and repository contracts. References only `Domain`. Zero EF Core or SQL leakage.
- **PharmaERP.Infrastructure**: Persistence implementation, `AppDbContext`, `IDbContextFactory`, EF Core SQL Server configurations, and repository implementations. References `Application` and `Domain`.
- **PharmaERP.Desktop**: WPF desktop presentation layer, MVVM architecture, Microsoft DI Host, theme dictionaries, and non-blocking shell. References `Application` and `Infrastructure`. Zero direct SQL or EF Core references.

---

## Database Setup & Deployment Modes

PharmaERP features an automated, production-grade first-run database setup experience. Configuration is managed in `%LocalAppData%\PharmaERP\settings.json` and can be reconfigured at any time from **Settings -> Database Connection Wizard**.

### 1. Single-PC Standalone Deployment
Designed for single-counter pharmacies where SQL Server and PharmaERP run on the same physical computer:
1. Install **SQL Server Express** (or SQL Server Developer/Standard).
2. Launch `PharmaERP.Desktop.exe`.
3. If no prior configuration exists, the **Database Setup Wizard** opens automatically.
4. Select **Local SQL Server Express** (`.\SQLEXPRESS`) or enter `(localdb)\mssqllocaldb`.
5. Select **Windows Integrated Authentication**.
6. Set Target Database to `PharmaERP` and click **Test Connection**.
7. If the database does not exist, click **Create Database & Run Migrations**. The system applies all EF Core migrations, seeds the default Chart of Accounts, initializes the operational accounting gate, and applies regional defaults (PK/PKR/Rs./en-PK/en).
8. Click **Save Configuration & Launch PharmaERP**.

### 2. Multi-PC Office LAN Deployment
Designed for multi-counter or back-office setups where multiple client PCs connect to a central database server:
- **Server PC Configuration**:
  1. Open **SQL Server Configuration Manager**:
     - Under *SQL Server Network Configuration -> Protocols for [INSTANCE]*, enable **TCP/IP**.
  2. Port Configuration:
     - **Option A (Static Port - Recommended)**: In *TCP/IP Properties -> IP Addresses*, scroll down to *IPAll*. Clear *TCP Dynamic Ports* (make it blank) and set *TCP Port* to a fixed port (e.g., **1433**). Then allow this inbound TCP port in Windows Defender Firewall. SQL Server Browser is not required if connecting via `HOST,PORT`.
     - **Option B (Named Instance Dynamic Port + SQL Browser)**: If SQL Server Express uses dynamic ports, allow incoming UDP port **1434** for the **SQL Server Browser** service (set service to *Automatic* and start it) so client PCs can resolve the named instance (e.g., `SERVER-PC\SQLEXPRESS`). Also allow inbound connections for the `sqlservr.exe` program or the configured dynamic port in Windows Firewall.
  3. Authentication Setup:
     - **Windows Authentication (Domain / AD)**: Recommended if all PCs are joined to an Active Directory domain. Grant the domain users access to the `PharmaERP` database with `db_datareader` and `db_datawriter` roles.
     - **SQL Server Authentication (Workgroup LAN)**: If PCs are in a standard Windows workgroup, enable *SQL Server and Windows Authentication mode* in SQL Server Management Studio. Create a dedicated SQL user (e.g., `pharma_app`) with appropriate database permissions.
- **Client Workstation Configuration**:
  1. Launch `PharmaERP.Desktop.exe` on each client workstation.
  2. In the **Database Setup Wizard**, choose **Office LAN Server** and enter the Server IP or Hostname (e.g., `192.168.1.100,1433` or `SERVER-PC\SQLEXPRESS`).
  3. Select Authentication type (Windows Auth or SQL Auth with username & password).
  4. Test Connection and save. Client workstations safely detect existing migrated schemas without running destructive migrations. LAN migration concurrency is guarded by SQL Server application locks (`sp_getapplock`).

### 3. Developer & Automated Deployment Precedence
Connection strings are resolved in the following priority at runtime:
1. **Environment Variable**: `PHARMAERP_CONNECTIONSTRING` (used in CI/CD, headless verification, and automated containers).
2. **Workstation User Settings**: `%LocalAppData%\PharmaERP\settings.json` (encrypted via Windows DPAPI for SQL passwords).
3. **Application Configuration**: `src/PharmaERP.Desktop/appsettings.json` (fallback active profile).

#### EF Core CLI Developer Migration Precedence
When running design-time EF Core tools (e.g., `dotnet ef migrations add`, `dotnet ef database update`), the design-time factory (`AppDbContextFactory`) strictly isolates development tooling from runtime client workstation settings:
1. `PHARMAERP_CONNECTIONSTRING` environment variable (explicit developer override).
2. Default local developer connection string: `Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True;`.
*(Note: Design-time EF tooling intentionally does NOT query `%LocalAppData%\PharmaERP\settings.json` to prevent developer tools from accidentally targeting or migrating end-user client databases).*

---

## Security & Credential Management

- **Zero Plaintext Passwords**: No SQL passwords or secrets are committed to source control or default config files.
- **Integrated Security Preferred**: Production deployments should prefer Windows / Integrated Authentication (`Integrated Security=True`).
- **Windows DPAPI Protection**: When SQL Authentication is selected in the wizard, passwords are encrypted on the local workstation using **Windows Data Protection API (DPAPI)** with `DataProtectionScope.CurrentUser` before saving to `%LocalAppData%\PharmaERP\settings.json`.
- **Credential Masking**: All connection test logs, UI diagnostics, and exception handlers strictly mask passwords (`Password=***`) to prevent shoulder surfing or log leakage.

---

## Project Structure

```
PharmaERP/
├── PharmaERP.slnx
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   └── PROJECT_STATUS.md
├── src/
│   ├── PharmaERP.Domain/           # Entities (Product, Batches, Invoices, Accounts, Vouchers, Journals)
│   ├── PharmaERP.Application/      # Interfaces, Services, DTOs, Settlement, PagedResult models
│   ├── PharmaERP.Infrastructure/   # AppDbContext, Migrations, Repositories, Writers, DB Config & Setup
│   └── PharmaERP.Desktop/          # WPF Views, ViewModels, First-Run Wizard, Resource Dictionaries
└── tests/
    ├── PharmaERP.Domain.Tests/         # Domain entity rules and concurrency token tests
    ├── PharmaERP.Application.Tests/    # Application service validation and business logic tests
    └── PharmaERP.Infrastructure.Tests/ # SQL Server Integration tests (Inventory, Sales, POS, Accounting, Setup)
```

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (v10.0.302 or later)
- SQL Server Express (`.\SQLEXPRESS`), SQL Server LocalDB, or LAN SQL Server instance

### Building the Solution
```powershell
dotnet restore PharmaERP.slnx
dotnet build PharmaERP.slnx --no-restore
```

### Running Tests
```powershell
dotnet test PharmaERP.slnx
```

### Launching the Desktop Application
```powershell
dotnet run --project src/PharmaERP.Desktop/PharmaERP.Desktop.csproj
```
If no database is configured, the setup wizard will immediately guide you through connection configuration and migration.

