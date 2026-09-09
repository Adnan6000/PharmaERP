using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleReturnItemConfiguration : IEntityTypeConfiguration<SaleReturnItem>
{
    public void Configure(EntityTypeBuilder<SaleReturnItem> builder)
    {
        builder.ToTable("SaleReturnItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Property(i => i.RefundAmount).HasPrecision(18, 4);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasOne(i => i.SaleReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.SaleReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.OriginalSaleInvoiceItem)
            .WithMany()
            .HasForeignKey(i => i.OriginalSaleInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.BatchAllocations)
            .WithOne(a => a.SaleReturnItem)
            .HasForeignKey(a => a.SaleReturnItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.SaleReturnId);
        builder.HasIndex(i => i.OriginalSaleInvoiceItemId);
        builder.HasIndex(i => i.ProductId);
    }
}

