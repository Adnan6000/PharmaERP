using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Infrastructure.Persistence;

namespace PharmaERP.Infrastructure.Tests;

public class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext() => new AppDbContext(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(_options));
}

public class SqlServerTestFixture : IAsyncLifetime
{
    public const string ConnectionString = "Server=.\\SQLEXPRESS;Database=PharmaERP_Test;Integrated Security=True;TrustServerCertificate=True";

    public IDbContextFactory<AppDbContext> ContextFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, b => b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        ContextFactory = new TestDbContextFactory(options);

        await using var context = await ContextFactory.CreateDbContextAsync();

        // Migrate to latest schema
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync();
            await context.Database.EnsureDeletedAsync();
        }
        catch
        {
            // Ignore teardown errors
        }
    }

    public async Task ResetDataAsync()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM SaleReturnBatchAllocations;
            DELETE FROM SaleReturnItems;
            DELETE FROM SaleReturns;
            DELETE FROM SaleInvoiceBatchAllocations;
            DELETE FROM SaleInvoiceItems;
            DELETE FROM SaleInvoices;
            DELETE FROM StockMovements;
            DELETE FROM PurchaseReturnItems;
            DELETE FROM PurchaseReturns;
            DELETE FROM PurchaseInvoiceItems;
            DELETE FROM PurchaseInvoices;
            DELETE FROM ProductBatches;
            DELETE FROM Products;
            DELETE FROM Suppliers;
            DELETE FROM Customers;
            DELETE FROM DocumentSequences;
            DELETE FROM AppConfigs;
        ");
    }
}

