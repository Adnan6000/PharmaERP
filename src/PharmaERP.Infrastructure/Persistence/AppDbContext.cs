using Microsoft.EntityFrameworkCore;
using PharmaERP.Domain.Common;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceItem> SaleInvoiceItems => Set<SaleInvoiceItem>();
    public DbSet<SaleInvoiceBatchAllocation> SaleInvoiceBatchAllocations => Set<SaleInvoiceBatchAllocation>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<SaleReturnBatchAllocation> SaleReturnBatchAllocations => Set<SaleReturnBatchAllocation>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<PharmaERP.Infrastructure.Persistence.Entities.DocumentSequence> DocumentSequences => Set<PharmaERP.Infrastructure.Persistence.Entities.DocumentSequence>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<ReceiptVoucher> ReceiptVouchers => Set<ReceiptVoucher>();
    public DbSet<PaymentVoucher> PaymentVouchers => Set<PaymentVoucher>();
    public DbSet<JournalVoucher> JournalVouchers => Set<JournalVoucher>();
    public DbSet<OpeningBalanceVoucher> OpeningBalanceVouchers => Set<OpeningBalanceVoucher>();
    public DbSet<ContraVoucher> ContraVouchers => Set<ContraVoucher>();
    public DbSet<ReceiptVoucherAllocation> ReceiptVoucherAllocations => Set<ReceiptVoucherAllocation>();
    public DbSet<PaymentVoucherAllocation> PaymentVoucherAllocations => Set<PaymentVoucherAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ensure BaseEntity is never mapped as an entity type or table
        modelBuilder.Ignore<BaseEntity>();

        // Apply all entity type configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

