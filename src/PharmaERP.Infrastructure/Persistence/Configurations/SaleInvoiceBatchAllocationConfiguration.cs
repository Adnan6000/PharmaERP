using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleInvoiceBatchAllocationConfiguration : IEntityTypeConfiguration<SaleInvoiceBatchAllocation>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceBatchAllocation> builder)
    {
        builder.ToTable("SaleInvoiceBatchAllocations");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.BatchNumberSnapshot)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(a => a.Quantity).HasPrecision(18, 4);
        builder.Property(a => a.InventoryCostRate).HasPrecision(18, 4);
        builder.Property(a => a.InventoryValueConsumed).HasPrecision(18, 4);
        builder.Property(a => a.ReturnedQuantity).HasPrecision(18, 4);
        builder.Property(a => a.ReturnedInventoryValue).HasPrecision(18, 4);

        builder.Property(a => a.RowVersion)
            .IsRowVersion();

        builder.HasOne(a => a.SaleInvoiceItem)
            .WithMany(i => i.BatchAllocations)
            .HasForeignKey(a => a.SaleInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ProductBatch)
            .WithMany()
            .HasForeignKey(a => a.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.SaleInvoiceItemId);
        builder.HasIndex(a => a.ProductBatchId);
    }
}

