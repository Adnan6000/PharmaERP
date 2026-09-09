using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleReturnBatchAllocationConfiguration : IEntityTypeConfiguration<SaleReturnBatchAllocation>
{
    public void Configure(EntityTypeBuilder<SaleReturnBatchAllocation> builder)
    {
        builder.ToTable("SaleReturnBatchAllocations");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Quantity).HasPrecision(18, 4);
        builder.Property(a => a.RestoredInventoryCostRate).HasPrecision(18, 4);
        builder.Property(a => a.RestoredInventoryValue).HasPrecision(18, 4);

        builder.Property(a => a.RowVersion)
            .IsRowVersion();

        builder.HasOne(a => a.SaleReturnItem)
            .WithMany(i => i.BatchAllocations)
            .HasForeignKey(a => a.SaleReturnItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.OriginalSaleInvoiceBatchAllocation)
            .WithMany()
            .HasForeignKey(a => a.OriginalSaleInvoiceBatchAllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ProductBatch)
            .WithMany()
            .HasForeignKey(a => a.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.SaleReturnItemId);
        builder.HasIndex(a => a.OriginalSaleInvoiceBatchAllocationId);
        builder.HasIndex(a => a.ProductBatchId);
    }
}

