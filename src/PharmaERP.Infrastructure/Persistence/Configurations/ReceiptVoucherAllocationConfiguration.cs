using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class ReceiptVoucherAllocationConfiguration : BaseEntityConfiguration<ReceiptVoucherAllocation>
{
    public override void Configure(EntityTypeBuilder<ReceiptVoucherAllocation> builder)
    {
        base.Configure(builder);

        builder.ToTable("ReceiptVoucherAllocations", tb =>
        {
            tb.HasCheckConstraint("CK_ReceiptVoucherAllocations_Amount_Positive", "[AllocatedAmount] > 0");
        });

        builder.Property(a => a.AllocatedAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.AllocatedAtUtc)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(PharmaERP.Domain.Enums.AllocationStatus.Active);

        builder.Property(a => a.VoidReason)
            .HasMaxLength(500);

        builder.HasIndex(a => new { a.ReceiptVoucherId, a.SaleInvoiceId });

        builder.HasOne(a => a.ReceiptVoucher)
            .WithMany(v => v.Allocations)
            .HasForeignKey(a => a.ReceiptVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.SaleInvoice)
            .WithMany(s => s.Allocations)
            .HasForeignKey(a => a.SaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
