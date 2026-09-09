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

## Database Configuration & Deployment Modes

Configuration is managed through `src/PharmaERP.Desktop/appsettings.json`. The active server can be switched without recompiling.

### Conceptual Profiles
| Profile | Database Server | Target Use Case | Authentication |
| :--- | :--- | :--- | :--- |
| **`Development`** | `(localdb)\mssqllocaldb` | Local developer workstations only (*never production*) | Windows Integrated |
| **`Local`** | `.\SQLEXPRESS` | Production single-PC pharmacy installation | Windows Integrated |
| **`LAN`** | `192.168.1.100,1433` | Production office LAN multi-workstation deployment | Windows Integrated / Domain |

### Switching Profiles
Edit `appsettings.json` in the application directory:
```json
{
  "DatabaseConfig": {
    "ActiveProfile": "Local"
  }
}
```
*Note: Profile changes take effect upon application restart.*

---

## Security & Credential Management

- **Zero Plaintext Passwords**: No SQL passwords or secrets are committed to source control or default config files.
- **Integrated Security Preferred**: Production deployments should prefer Windows / Integrated Authentication (`Integrated Security=True`).
- **SQL Server Authentication**: When SQL Authentication is necessary, credentials must not be stored in plaintext. Use **Windows Credential Manager** or **Windows DPAPI** to protect connection credentials, or provide runtime environment variable overrides (`PHARMAERP_CONNECTIONSTRING`) for deployment pipelines.

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
│   ├── PharmaERP.Domain/           # Entities (Product, Manufacturer, Category, Unit) & BaseEntity
│   ├── PharmaERP.Application/      # IProductRepository, IProductService, Models (PagedResult)
│   ├── PharmaERP.Infrastructure/   # AppDbContext, Configurations, ProductRepository, ConnectionTester
│   └── PharmaERP.Desktop/          # WPF Views, ViewModels, Commands, Resource Dictionaries
└── tests/
    ├── PharmaERP.Domain.Tests/     # Domain entity rules and concurrency token tests
    └── PharmaERP.Application.Tests/# Application service logic and pagination tests
```

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (v10.0.302 or later)
- SQL Server Express (`.\SQLEXPRESS`) or SQL Server LocalDB for development

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

