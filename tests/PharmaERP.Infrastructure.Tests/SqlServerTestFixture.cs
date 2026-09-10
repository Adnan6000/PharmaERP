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

    public AppDbContext CreateDbContext() => new(_options);

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
            IF OBJECT_ID('TR_JournalEntries_PreventPostedHeaderModifications', 'TR') IS NOT NULL
                DISABLE TRIGGER TR_JournalEntries_PreventPostedHeaderModifications ON JournalEntries;
            IF OBJECT_ID('TR_JournalEntryLines_PreventPostedModifications', 'TR') IS NOT NULL
                DISABLE TRIGGER TR_JournalEntryLines_PreventPostedModifications ON JournalEntryLines;

            DELETE FROM ReceiptVoucherAllocations;
            DELETE FROM PaymentVoucherAllocations;
            DELETE FROM ContraVouchers;
            DELETE FROM JournalEntryLines;
            DELETE FROM JournalEntries;
            DELETE FROM ReceiptVouchers;
            DELETE FROM PaymentVouchers;
            DELETE FROM JournalVouchers;
            DELETE FROM OpeningBalanceVouchers;
            DELETE FROM Accounts;

            IF OBJECT_ID('TR_JournalEntries_PreventPostedHeaderModifications', 'TR') IS NOT NULL
                ENABLE TRIGGER TR_JournalEntries_PreventPostedHeaderModifications ON JournalEntries;
            IF OBJECT_ID('TR_JournalEntryLines_PreventPostedModifications', 'TR') IS NOT NULL
                ENABLE TRIGGER TR_JournalEntryLines_PreventPostedModifications ON JournalEntryLines;

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

    public PharmaERP.Application.Interfaces.IAccountingOperationalGate CreateAccountingGate() =>
        new PharmaERP.Infrastructure.Persistence.Services.AccountingOperationalGate(ContextFactory);

    public PharmaERP.Application.Common.Interfaces.IAccountingTransactionWriter CreateAccountingWriter()
    {
        var configRepo = new PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository>.Instance);
        var clock = new PharmaERP.Infrastructure.Persistence.Services.BusinessClock(ContextFactory, configRepo);
        var accountRepo = new PharmaERP.Infrastructure.Persistence.Repositories.AccountRepository(ContextFactory);
        var docNumGen = new PharmaERP.Infrastructure.Persistence.Services.DocumentNumberGenerator(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Services.DocumentNumberGenerator>.Instance);
        return new PharmaERP.Infrastructure.Persistence.Services.AccountingTransactionWriter(ContextFactory, clock, configRepo, accountRepo, docNumGen, NullLogger<PharmaERP.Infrastructure.Persistence.Services.AccountingTransactionWriter>.Instance);
    }

    public PharmaERP.Infrastructure.Persistence.Services.InventoryTransactionWriter CreateInventoryWriter() =>
        new(ContextFactory, CreateAccountingGate(), CreateAccountingWriter(), NullLogger<PharmaERP.Infrastructure.Persistence.Services.InventoryTransactionWriter>.Instance);

    public PharmaERP.Infrastructure.Persistence.Services.PurchaseTransactionWriter CreatePurchaseWriter() =>
        new(ContextFactory, CreateAccountingGate(), CreateAccountingWriter(), NullLogger<PharmaERP.Infrastructure.Persistence.Services.PurchaseTransactionWriter>.Instance);

    public PharmaERP.Infrastructure.Persistence.Services.SaleTransactionWriter CreateSaleWriter()
    {
        var configRepo = new PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository>.Instance);
        var clock = new PharmaERP.Infrastructure.Persistence.Services.BusinessClock(ContextFactory, configRepo);
        return new PharmaERP.Infrastructure.Persistence.Services.SaleTransactionWriter(ContextFactory, configRepo, clock, CreateAccountingGate(), CreateAccountingWriter(), NullLogger<PharmaERP.Infrastructure.Persistence.Services.SaleTransactionWriter>.Instance);
    }

    public PharmaERP.Application.Common.Interfaces.IAccountRepository CreateAccountRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.AccountRepository(ContextFactory);

    public PharmaERP.Application.Common.Interfaces.IJournalRepository CreateJournalRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.JournalRepository(ContextFactory);

    public PharmaERP.Application.Common.Interfaces.IVoucherRepository CreateVoucherRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.VoucherRepository(ContextFactory);

    public PharmaERP.Application.Common.Interfaces.IAppConfigRepository CreateAppConfigRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.AppConfigRepository>.Instance);

    public PharmaERP.Application.Common.Interfaces.IBusinessClock CreateBusinessClock() =>
        new PharmaERP.Infrastructure.Persistence.Services.BusinessClock(ContextFactory, CreateAppConfigRepository());

    public PharmaERP.Application.Common.Interfaces.IDocumentNumberGenerator CreateDocumentNumberGenerator() =>
        new PharmaERP.Infrastructure.Persistence.Services.DocumentNumberGenerator(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Services.DocumentNumberGenerator>.Instance);

    public PharmaERP.Application.Common.Interfaces.ICustomerRepository CreateCustomerRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.CustomerRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.CustomerRepository>.Instance);

    public PharmaERP.Application.Common.Interfaces.ISupplierRepository CreateSupplierRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.SupplierRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.SupplierRepository>.Instance);

    public PharmaERP.Application.Common.Interfaces.IProductBatchRepository CreateProductBatchRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.ProductBatchRepository(ContextFactory, NullLogger<PharmaERP.Infrastructure.Persistence.Repositories.ProductBatchRepository>.Instance);

    public PharmaERP.Application.Interfaces.IAccountService CreateAccountService() =>
        new PharmaERP.Application.Services.AccountService(CreateAccountRepository(), CreateAppConfigRepository());

    public PharmaERP.Application.Interfaces.IAccountingConfigService CreateAccountingConfigService() =>
        new PharmaERP.Application.Services.AccountingConfigService(CreateAppConfigRepository(), CreateAccountRepository());

    public PharmaERP.Application.Interfaces.IVoucherService CreateVoucherService() =>
        new PharmaERP.Application.Services.VoucherService(CreateVoucherRepository(), CreateAccountRepository(), CreateAccountingWriter(), CreateDocumentNumberGenerator(), CreateAccountingConfigService());

    public PharmaERP.Application.Interfaces.IJournalService CreateJournalService() =>
        new PharmaERP.Application.Services.JournalService(CreateJournalRepository(), CreateAccountRepository());

    public PharmaERP.Application.Interfaces.IPartyLedgerService CreatePartyLedgerService() =>
        new PharmaERP.Application.Services.PartyLedgerService(CreateJournalRepository(), CreateAccountRepository(), CreateCustomerRepository(), CreateSupplierRepository());

    public PharmaERP.Application.Interfaces.IAccountingReconciliationService CreateReconciliationService() =>
        new PharmaERP.Application.Services.AccountingReconciliationService(CreateJournalRepository(), CreateAccountRepository(), CreateProductBatchRepository());

    public PharmaERP.Application.Interfaces.IAccountingSetupService CreateAccountingSetupService()
    {
        var runner = new PharmaERP.Infrastructure.Persistence.Services.HistoricalBackfillRunner(
            ContextFactory,
            CreateBusinessClock(),
            CreateAccountRepository(),
            CreateDocumentNumberGenerator(),
            NullLogger<PharmaERP.Infrastructure.Persistence.Services.HistoricalBackfillRunner>.Instance);

        return new PharmaERP.Application.Services.AccountingSetupService(
            CreateAccountRepository(),
            CreateAppConfigRepository(),
            CreateAccountingGate(),
            CreateReconciliationService(),
            CreateBusinessClock(),
            CreateJournalRepository(),
            CreateAccountingWriter(),
            CreateDocumentNumberGenerator(),
            runner,
            NullLogger<PharmaERP.Application.Services.AccountingSetupService>.Instance);
    }

    public PharmaERP.Application.Common.Interfaces.ISettlementRepository CreateSettlementRepository() =>
        new PharmaERP.Infrastructure.Persistence.Repositories.SettlementRepository(ContextFactory);

    public PharmaERP.Application.Interfaces.IPaymentSettlementService CreateSettlementService() =>
        new PharmaERP.Application.Services.PaymentSettlementService(CreateSettlementRepository());

    public PharmaERP.Application.Interfaces.IRegionalSettingsService CreateRegionalSettingsService() =>
        new PharmaERP.Application.Services.RegionalSettingsService(CreateAppConfigRepository());
}

