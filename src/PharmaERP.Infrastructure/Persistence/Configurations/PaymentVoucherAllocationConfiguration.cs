using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class PaymentVoucherAllocationConfiguration : BaseEntityConfiguration<PaymentVoucherAllocation>
{
    public override void Configure(EntityTypeBuilder<PaymentVoucherAllocation> builder)
    {
        base.Configure(builder);

        builder.ToTable("PaymentVoucherAllocations", tb =>
        {
            tb.HasCheckConstraint("CK_PaymentVoucherAllocations_Amount_Positive", "[AllocatedAmount] > 0");
        });

        builder.Property(a => a.AllocatedAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.AllocatedAtUtc)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(PharmaERP.Domain.Enums.AllocationStatus.Active)
            .HasSentinel((PharmaERP.Domain.Enums.AllocationStatus)0);

        builder.Property(a => a.VoidReason)
            .HasMaxLength(500);

        builder.HasIndex(a => new { a.PaymentVoucherId, a.PurchaseInvoiceId });

        builder.HasOne(a => a.PaymentVoucher)
            .WithMany(v => v.Allocations)
            .HasForeignKey(a => a.PaymentVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.PurchaseInvoice)
            .WithMany(p => p.Allocations)
            .HasForeignKey(a => a.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
