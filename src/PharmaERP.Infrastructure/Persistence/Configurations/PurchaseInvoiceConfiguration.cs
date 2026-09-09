using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("PurchaseInvoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.SupplierInvoiceNumber)
            .HasMaxLength(50);

        builder.Property(i => i.NormalizedSupplierInvoiceNumber)
            .HasMaxLength(50);

        builder.Property(i => i.Remarks)
            .HasMaxLength(300);

        builder.Property(i => i.CancellationReason)
            .HasMaxLength(300);

        builder.Property(i => i.GrossTotal)
            .HasPrecision(18, 4);

        builder.Property(i => i.DiscountAmount)
            .HasPrecision(18, 4);

        builder.Property(i => i.TaxAmount)
            .HasPrecision(18, 4);

        builder.Property(i => i.NetTotal)
            .HasPrecision(18, 4);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasOne(i => i.Supplier)
            .WithMany(s => s.PurchaseInvoices)
            .HasForeignKey(i => i.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique internal invoice sequence number
        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique();

        // Unique filtered index preventing duplicate vendor invoice entry per supplier
        builder.HasIndex(i => new { i.SupplierId, i.NormalizedSupplierInvoiceNumber })
            .IsUnique()
            .HasFilter("[NormalizedSupplierInvoiceNumber] IS NOT NULL AND [NormalizedSupplierInvoiceNumber] <> ''");

        // Query indexes
        builder.HasIndex(i => new { i.SupplierId, i.InvoiceDate });
        builder.HasIndex(i => i.InvoiceDate);
    }
}

