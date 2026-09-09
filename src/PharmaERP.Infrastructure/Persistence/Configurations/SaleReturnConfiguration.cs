using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("SaleReturns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReturnNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.CustomerNameSnapshot)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(r => r.Reason)
            .HasMaxLength(300);

        builder.Property(r => r.CancellationReason)
            .HasMaxLength(300);

        builder.Property(r => r.TotalAmount).HasPrecision(18, 4);

        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        builder.HasOne(r => r.OriginalSaleInvoice)
            .WithMany()
            .HasForeignKey(r => r.OriginalSaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.SaleReturn)
            .HasForeignKey(i => i.SaleReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ReturnNumber).IsUnique();
        builder.HasIndex(r => r.OperationId).IsUnique();
        builder.HasIndex(r => r.OriginalSaleInvoiceId);
        builder.HasIndex(r => new { r.ReturnDate, r.Status });
    }
}

