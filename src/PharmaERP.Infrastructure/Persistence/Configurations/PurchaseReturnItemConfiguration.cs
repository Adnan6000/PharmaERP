using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class PurchaseReturnItemConfiguration : IEntityTypeConfiguration<PurchaseReturnItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnItem> builder)
    {
        builder.ToTable("PurchaseReturnItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity)
            .HasPrecision(18, 4);

        builder.Property(i => i.ReturnRate)
            .HasPrecision(18, 4);

        builder.Property(i => i.LineTotal)
            .HasPrecision(18, 4);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasOne(i => i.PurchaseReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.PurchaseReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.OriginalPurchaseInvoiceItem)
            .WithMany()
            .HasForeignKey(i => i.OriginalPurchaseInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductBatch)
            .WithMany()
            .HasForeignKey(i => i.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.PurchaseReturnId);
        builder.HasIndex(i => i.OriginalPurchaseInvoiceItemId);
        builder.HasIndex(i => i.ProductId);
        builder.HasIndex(i => i.ProductBatchId);
    }
}

