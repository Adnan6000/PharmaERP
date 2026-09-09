using Microsoft.Extensions.DependencyInjection;
using PharmaERP.Application.Services;

namespace PharmaERP.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers stateless Application services.
    /// In accordance with WPF desktop architecture rules, services are registered as Transient.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<IProductService, ProductService>();
        services.AddTransient<IManufacturerService, ManufacturerService>();
        services.AddTransient<ICategoryService, CategoryService>();
        services.AddTransient<IUnitService, UnitService>();
        services.AddTransient<IDashboardService, DashboardService>();
        services.AddTransient<ICustomerService, CustomerService>();
        services.AddTransient<ISupplierService, SupplierService>();
        services.AddTransient<IInventoryService, InventoryService>();
        services.AddTransient<IPurchaseService, PurchaseService>();
        services.AddTransient<ISaleService, SaleService>();
        services.AddTransient<ISaleReturnService, SaleReturnService>();
        services.AddTransient<IInvoicePrintDataProvider, InvoicePrintDataProvider>();
        services.AddTransient<IProductLookupService, ProductLookupService>();

        return services;
    }
}

