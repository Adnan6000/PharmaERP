using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.ToTable("PurchaseReturns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReturnNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Reason)
            .HasMaxLength(300);

        builder.Property(r => r.TotalAmount)
            .HasPrecision(18, 4);

        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        builder.HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OriginalPurchaseInvoice)
            .WithMany()
            .HasForeignKey(r => r.OriginalPurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ReturnNumber)
            .IsUnique();

        builder.HasIndex(r => new { r.SupplierId, r.ReturnDate });
        builder.HasIndex(r => r.ReturnDate);
    }
}

