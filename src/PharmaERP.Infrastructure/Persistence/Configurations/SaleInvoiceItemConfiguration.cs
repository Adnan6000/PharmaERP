using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleInvoiceItemConfiguration : IEntityTypeConfiguration<SaleInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceItem> builder)
    {
        builder.ToTable("SaleInvoiceItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductCodeSnapshot)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.ProductNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.UnitSnapshot)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Property(i => i.UnitSalePrice).HasPrecision(18, 4);
        builder.Property(i => i.GrossAmount).HasPrecision(18, 4);
        builder.Property(i => i.LineDiscountAmount).HasPrecision(18, 4);
        builder.Property(i => i.InvoiceDiscountAllocated).HasPrecision(18, 4);
        builder.Property(i => i.TaxAllocated).HasPrecision(18, 4);
        builder.Property(i => i.NetLineAmount).HasPrecision(18, 4);
        builder.Property(i => i.ReturnedQuantity).HasPrecision(18, 4);
        builder.Property(i => i.RefundedAmount).HasPrecision(18, 4);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasOne(i => i.SaleInvoice)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.BatchAllocations)
            .WithOne(a => a.SaleInvoiceItem)
            .HasForeignKey(a => a.SaleInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.SaleInvoiceId);
        builder.HasIndex(i => i.ProductId);
    }
}

