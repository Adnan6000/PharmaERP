using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable("ProductBatches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchNumber)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(b => b.NormalizedBatchNumber)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(b => b.QuantityOnHand)
            .HasPrecision(18, 4)
            .HasDefaultValue(0.0000m);

        builder.Property(b => b.InventoryValue)
            .HasPrecision(18, 4)
            .HasDefaultValue(0.0000m);

        builder.Property(b => b.AveragePurchaseCost)
            .HasPrecision(18, 4)
            .HasDefaultValue(0.0000m);

        builder.Property(b => b.LastPurchaseCost)
            .HasPrecision(18, 4)
            .HasDefaultValue(0.0000m);

        builder.Property(b => b.SuggestedSalePrice)
            .HasPrecision(18, 4);

        builder.Property(b => b.RowVersion)
            .IsRowVersion();

        builder.HasOne(b => b.Product)
            .WithMany(p => p.Batches)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique composite index: (ProductId, NormalizedBatchNumber, ExpiryDate)
        builder.HasIndex(b => new { b.ProductId, b.NormalizedBatchNumber, b.ExpiryDate })
            .IsUnique();

        // Query-driven index for FEFO sorting
        builder.HasIndex(b => new { b.ProductId, b.ExpiryDate });
    }
}

