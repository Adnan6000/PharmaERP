using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaERP.Application;
using PharmaERP.Desktop.Services;
using PharmaERP.Desktop.ViewModels;
using PharmaERP.Infrastructure;

namespace PharmaERP.Desktop;

/// <summary>
/// Interaction logic for App.xaml with Microsoft DI Host configuration.
/// Startup is non-blocking and immediate; no synchronous database operations occur on startup.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host?.Services ?? throw new InvalidOperationException("Host not started.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder(e.Args)
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
                services.AddInfrastructureServices(context.Configuration);

                // Desktop Services & State Stores
                services.AddSingleton<ConnectionStateStore>();
                services.AddSingleton<WorkstationConfigService>();
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
                services.AddTransient<MainWindowViewModel>();

                // Views
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Resolve MainWindow through DI container
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
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

