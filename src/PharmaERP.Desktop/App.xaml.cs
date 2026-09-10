using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Application;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Interfaces;
using PharmaERP.Desktop.Services;
using PharmaERP.Desktop.ViewModels;
using PharmaERP.Desktop.Views;
using PharmaERP.Infrastructure;
using PharmaERP.Infrastructure.Persistence;
using PharmaERP.Infrastructure.Security;

namespace PharmaERP.Desktop;

/// <summary>
/// Interaction logic for App.xaml with lightweight bootstrap and Microsoft DI Host configuration.
/// Ensures that no normal ERP data-loading ViewModels touch the database until connectivity,
/// schema migration, and baseline configuration are verified.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host not started.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent application from automatically terminating when the setup dialog closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // -----------------------------------------------------------------------
        // 1. LIGHTWEIGHT BOOTSTRAP PREFLIGHT
        // -----------------------------------------------------------------------
        var configStore = new WorkstationDatabaseConfigStore();
        var credentialProtector = new DpapiCredentialProtector();
        var connectionStringFactory = new SqlConnectionStringFactory(credentialProtector);

        var dbConfig = configStore.LoadConfig();
        bool isDbReady = false;
        string? activeConnStr = null;

        if (dbConfig != null && dbConfig.IsConfigured)
        {
            activeConnStr = connectionStringFactory.BuildConnectionString(dbConfig);
            var dummyConfig = new ConfigurationBuilder().Build();
            var tester = new DbConnectionTester(
                new SimpleDbContextFactory(activeConnStr),
                dummyConfig,
                connectionStringFactory,
                configStore,
                NullLogger<DbConnectionTester>.Instance);

            var testResult = await tester.TestConfigAsync(dbConfig);
            if (testResult.IsConnected && testResult.IsDatabaseInitialized)
            {
                isDbReady = true;
            }
            else if (testResult.IsConnected && testResult.PendingMigrationsCount > 0)
            {
                // Connected but pending schema migrations exist: upgrade safely
                var migrator = new DatabaseMigrator(
                    new SimpleDbContextFactory(activeConnStr),
                    NullLogger<DatabaseMigrator>.Instance);
                var migResult = await migrator.MigrateDatabaseAsync(activeConnStr);
                isDbReady = migResult.Success;
            }
        }

        // -----------------------------------------------------------------------
        // 2. IF DATABASE IS NOT READY / NO CONFIG: OPEN DATABASE SETUP DIALOG FIRST
        // -----------------------------------------------------------------------
        if (!isDbReady)
        {
            var initialConnStr = activeConnStr
                ?? "Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True";

            var migrator = new DatabaseMigrator(
                new SimpleDbContextFactory(initialConnStr),
                NullLogger<DatabaseMigrator>.Instance);

            var dummyConfig = new ConfigurationBuilder().Build();
            var tester = new DbConnectionTester(
                new SimpleDbContextFactory(initialConnStr),
                dummyConfig,
                connectionStringFactory,
                configStore,
                NullLogger<DbConnectionTester>.Instance);

            var setupVm = new DatabaseSetupViewModel(
                tester,
                migrator,
                configStore,
                credentialProtector,
                connectionStringFactory);

            var setupWindow = new DatabaseSetupWindow(setupVm);
            bool? dialogResult = setupWindow.ShowDialog();

            if (dialogResult != true)
            {
                // User cancelled setup or closed window -> clean exit
                Shutdown(0);
                return;
            }

            // User successfully configured & initialized database!
            dbConfig = configStore.LoadConfig();
            if (dbConfig != null && dbConfig.IsConfigured)
            {
                activeConnStr = connectionStringFactory.BuildConnectionString(dbConfig);
            }
        }

        // -----------------------------------------------------------------------
        // 3. DATABASE IS VERIFIED: CONSTRUCT FULL ERP HOST & START APPLICATION
        // -----------------------------------------------------------------------
        await StartFullHostAndShowMainWindowAsync(e.Args, activeConnStr);
    }

    private async Task StartFullHostAndShowMainWindowAsync(string[] args, string? activeConnectionString)
    {
        _host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                // Application Services (Transient)
                services.AddApplicationServices();

                // Infrastructure Services (IDbContextFactory, Transient Repositories, Migrator)
                services.AddInfrastructureServices(context.Configuration, activeConnectionString);

                // Desktop Services & State Stores
                services.AddSingleton<ConnectionStateStore>();
                services.AddSingleton<WorkstationConfigService>();
                services.AddSingleton<ICurrencyFormatter, CurrencyFormatter>();
                services.AddSingleton<ILanguageService, LanguageService>();
                services.AddTransient<InvoicePrintService>();

                // Desktop ViewModels (Transient / Single window lifetime)
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<SalesEntryViewModel>();
                services.AddTransient<SalesViewModel>();
                services.AddTransient<SalesReturnsViewModel>();
                services.AddTransient<ProductsViewModel>();
                services.AddTransient<CustomersViewModel>();
                services.AddTransient<SuppliersViewModel>();
                services.AddTransient<PurchaseEntryViewModel>();
                services.AddTransient<PurchasesViewModel>();
                services.AddTransient<PurchaseReturnsViewModel>();
                services.AddTransient<InventoryStockViewModel>();
                services.AddTransient<OpeningStockViewModel>();
                services.AddTransient<ManufacturersViewModel>();
                services.AddTransient<CategoriesViewModel>();
                services.AddTransient<UnitsViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ChartOfAccountsViewModel>();
                services.AddTransient<VouchersViewModel>();
                services.AddTransient<LedgersViewModel>();
                services.AddTransient<AccountingSetupViewModel>();
                services.AddTransient<MainWindowViewModel>();

                // Views
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Initialize ambient currency and language infrastructure
        var currencyFormatter = _host.Services.GetRequiredService<ICurrencyFormatter>();
        AppCurrency.Initialize(currencyFormatter);

        var languageService = _host.Services.GetRequiredService<ILanguageService>();
        AppLanguage.Initialize(languageService);

        // Load company regional settings
        try
        {
            var regionalService = _host.Services.GetRequiredService<IRegionalSettingsService>();
            var settings = await regionalService.GetRegionalSettingsAsync();
            currencyFormatter.UpdateSettings(settings);
        }
        catch
        {
            // Default PK / PKR settings remain established
        }

        // Resolve MainWindow through DI container
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.FlowDirection = languageService.CurrentFlowDirection;
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

