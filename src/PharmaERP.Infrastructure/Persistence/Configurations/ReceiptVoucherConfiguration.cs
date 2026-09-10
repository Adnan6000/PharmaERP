using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class ReceiptVoucherConfiguration : BaseEntityConfiguration<ReceiptVoucher>
{
    public override void Configure(EntityTypeBuilder<ReceiptVoucher> builder)
    {
        base.Configure(builder);

        builder.ToTable("ReceiptVouchers");

        builder.Property(v => v.VoucherNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(v => v.BusinessDate)
            .IsRequired();

        builder.Property(v => v.PostedAtUtc)
            .IsRequired();

        builder.Property(v => v.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(v => v.Reference)
            .HasMaxLength(100);

        builder.Property(v => v.Narration)
            .HasMaxLength(500);

        builder.Property(v => v.Status)
            .IsRequired();

        builder.Property(v => v.OperationId)
            .IsRequired();

        builder.Property(v => v.CancellationReason)
            .HasMaxLength(500);

        builder.HasIndex(v => v.VoucherNumber)
            .IsUnique();

        builder.HasIndex(v => v.OperationId)
            .IsUnique();

        builder.HasIndex(v => new { v.BusinessDate, v.Id });

        builder.HasOne(v => v.CashOrBankAccount)
            .WithMany()
            .HasForeignKey(v => v.CashOrBankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.OffsetAccount)
            .WithMany()
            .HasForeignKey(v => v.OffsetAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Customer)
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
