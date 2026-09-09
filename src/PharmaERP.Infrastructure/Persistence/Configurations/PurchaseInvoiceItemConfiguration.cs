using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class PurchaseInvoiceItemConfiguration : IEntityTypeConfiguration<PurchaseInvoiceItem>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceItem> builder)
    {
        builder.ToTable("PurchaseInvoiceItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.BatchNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Quantity)
            .HasPrecision(18, 4);

        builder.Property(i => i.PurchaseRate)
            .HasPrecision(18, 4);

        builder.Property(i => i.SuggestedSaleRate)
            .HasPrecision(18, 4);

        builder.Property(i => i.DiscountAmount)
            .HasPrecision(18, 4);

        builder.Property(i => i.LineTotal)
            .HasPrecision(18, 4);

        builder.Property(i => i.ReturnedQuantity)
            .HasPrecision(18, 4);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasOne(i => i.PurchaseInvoice)
            .WithMany(inv => inv.Items)
            .HasForeignKey(i => i.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductBatch)
            .WithMany()
            .HasForeignKey(i => i.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.PurchaseInvoiceId);
        builder.HasIndex(i => i.ProductId);
        builder.HasIndex(i => i.ProductBatchId);
    }
}

