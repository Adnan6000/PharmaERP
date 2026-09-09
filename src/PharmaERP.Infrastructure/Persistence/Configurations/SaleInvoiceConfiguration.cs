using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class SaleInvoiceConfiguration : IEntityTypeConfiguration<SaleInvoice>
{
    public void Configure(EntityTypeBuilder<SaleInvoice> builder)
    {
        builder.ToTable("SaleInvoices");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.CustomerNameSnapshot)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(s => s.Reference)
            .HasMaxLength(100);

        builder.Property(s => s.Remarks)
            .HasMaxLength(300);

        builder.Property(s => s.CancellationReason)
            .HasMaxLength(300);

        // Historical Print Identity Snapshots
        builder.Property(s => s.PharmacyNameSnapshot)
            .HasMaxLength(200);

        builder.Property(s => s.PharmacyAddressSnapshot)
            .HasMaxLength(300);

        builder.Property(s => s.PharmacyPhoneSnapshot)
            .HasMaxLength(50);

        builder.Property(s => s.DrugLicenseNoSnapshot)
            .HasMaxLength(100);

        builder.Property(s => s.TaxNumberSnapshot)
            .HasMaxLength(100);

        builder.Property(s => s.ReceiptFooterSnapshot)
            .HasMaxLength(300);

        // Decimal precisions
        builder.Property(s => s.GrossTotal).HasPrecision(18, 4);
        builder.Property(s => s.LineDiscountTotal).HasPrecision(18, 4);
        builder.Property(s => s.InvoiceDiscountAmount).HasPrecision(18, 4);
        builder.Property(s => s.TaxAmount).HasPrecision(18, 4);
        builder.Property(s => s.NetTotal).HasPrecision(18, 4);
        builder.Property(s => s.TenderedAmount).HasPrecision(18, 4);
        builder.Property(s => s.ChangeGiven).HasPrecision(18, 4);

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Audit delete behavior: Restrict to prevent accidental cascade deletion of audit records
        builder.HasMany(s => s.Items)
            .WithOne(i => i.SaleInvoice)
            .HasForeignKey(i => i.SaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique document numbers and idempotency key
        builder.HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.HasIndex(s => s.OperationId).IsUnique();

        // Query indexes
        builder.HasIndex(s => new { s.BusinessDate, s.Status });
        builder.HasIndex(s => new { s.CustomerId, s.BusinessDate });
    }
}

