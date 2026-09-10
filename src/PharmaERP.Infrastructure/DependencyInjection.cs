using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Infrastructure.Persistence;
using PharmaERP.Infrastructure.Persistence.Repositories;
using PharmaERP.Infrastructure.Security;

namespace PharmaERP.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure persistence services, DbContextFactory, migrator, and repositories.
    /// In accordance with WPF desktop architecture rules:
    /// - DbContexts are created through a pooled factory (IDbContextFactory) and are short-lived.
    /// - Repositories and utility services are registered as Transient.
    /// - Migrations are NOT executed automatically at startup.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? explicitConnectionString = null)
    {
        // Core security and connection string factory
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.AddSingleton<IConnectionStringFactory, SqlConnectionStringFactory>();
        services.AddSingleton<IDatabaseConfigStore, WorkstationDatabaseConfigStore>();

        var connectionString = explicitConnectionString ?? ResolveConnectionString(configuration);

        services.AddPooledDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        // Repositories & utilities registered as Transient for desktop MVVM consumers
        services.AddTransient<IProductRepository, ProductRepository>();
        services.AddTransient<IManufacturerRepository, ManufacturerRepository>();
        services.AddTransient<ICategoryRepository, CategoryRepository>();
        services.AddTransient<IUnitRepository, UnitRepository>();
        services.AddTransient<IDashboardRepository, DashboardRepository>();
        services.AddTransient<IDbConnectionTester, DbConnectionTester>();
        services.AddTransient<IDatabaseMigrator, DatabaseMigrator>();

        // Milestone 3 Repositories
        services.AddTransient<ICustomerRepository, CustomerRepository>();
        services.AddTransient<ISupplierRepository, SupplierRepository>();
        services.AddTransient<IProductBatchRepository, ProductBatchRepository>();
        services.AddTransient<IStockMovementRepository, StockMovementRepository>();
        services.AddTransient<IPurchaseInvoiceRepository, PurchaseInvoiceRepository>();
        services.AddTransient<IPurchaseReturnRepository, PurchaseReturnRepository>();

        // Milestone 3 Document Numbering & Transaction Writers
        services.AddTransient<IDocumentNumberGenerator, Persistence.Services.DocumentNumberGenerator>();
        services.AddTransient<IPurchaseTransactionWriter, Persistence.Services.PurchaseTransactionWriter>();
        services.AddTransient<IInventoryTransactionWriter, Persistence.Services.InventoryTransactionWriter>();

        // Milestone 4 Repositories & Transaction Writers
        services.AddTransient<IAppConfigRepository, AppConfigRepository>();
        services.AddTransient<IBusinessClock, Persistence.Services.BusinessClock>();
        services.AddTransient<ISaleInvoiceRepository, SaleInvoiceRepository>();
        services.AddTransient<ISaleReturnRepository, SaleReturnRepository>();
        services.AddTransient<ISaleTransactionWriter, Persistence.Services.SaleTransactionWriter>();

        // Milestone 5 Accounting Persistence & Services
        services.AddTransient<IAccountRepository, AccountRepository>();
        services.AddTransient<IJournalRepository, JournalRepository>();
        services.AddTransient<IVoucherRepository, VoucherRepository>();
        services.AddTransient<ISettlementRepository, SettlementRepository>();
        services.AddTransient<Application.Interfaces.IAccountingOperationalGate, Persistence.Services.AccountingOperationalGate>();
        services.AddTransient<IAccountingTransactionWriter, Persistence.Services.AccountingTransactionWriter>();
        services.AddTransient<IHistoricalBackfillRunner, Persistence.Services.HistoricalBackfillRunner>();

        return services;
    }

    public static string ResolveActiveProfile(IConfiguration configuration)
    {
        // 1. Check local user settings file under %LocalAppData%\PharmaERP\settings.json
        try
        {
            var store = new WorkstationDatabaseConfigStore();
            var config = store.LoadConfig();
            if (config != null && !string.IsNullOrWhiteSpace(config.ActiveProfile))
            {
                return config.ActiveProfile;
            }
        }
        catch
        {
            // Fall back to configuration
        }

        // 2. Fallback to appsettings.json
        return configuration["DatabaseConfig:ActiveProfile"] ?? "Local";
    }

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        // 1. Check for runtime environment variable override (e.g. CI/CD or deployment override)
        var envOverride = Environment.GetEnvironmentVariable("PHARMAERP_CONNECTIONSTRING");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        // 2. Check canonical workstation settings in %LocalAppData%\PharmaERP\settings.json
        try
        {
            var store = new WorkstationDatabaseConfigStore();
            var config = store.LoadConfig();
            if (config != null && config.IsConfigured)
            {
                var protector = new DpapiCredentialProtector();
                var factory = new SqlConnectionStringFactory(protector);
                return factory.BuildConnectionString(config);
            }
        }
        catch
        {
            // Fall back to configuration
        }

        // 3. Resolve based on active profile (Development, Local, LAN) in appsettings.json
        var activeProfile = ResolveActiveProfile(configuration);
        var profileConnection = configuration[$"DatabaseConfig:Profiles:{activeProfile}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(profileConnection))
        {
            return profileConnection;
        }

        // 4. Fallback to default connection string in appsettings.json
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(defaultConnection))
        {
            return defaultConnection;
        }

        // Safe fallback for design-time or unconfigured initial start (SQL Express default)
        return "Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True";
    }
}

