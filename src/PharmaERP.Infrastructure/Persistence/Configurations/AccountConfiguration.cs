using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : BaseEntityConfiguration<Account>
{
    public override void Configure(EntityTypeBuilder<Account> builder)
    {
        base.Configure(builder);

        builder.ToTable("Accounts");

        builder.Property(a => a.AccountCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.AccountType)
            .IsRequired();

        builder.Property(a => a.AllowPosting)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.IsControlAccount)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.IsCashAccount)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.IsBankAccount)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.Description)
            .HasMaxLength(500);

        builder.HasIndex(a => a.AccountCode)
            .IsUnique();

        builder.HasIndex(a => a.SystemAccountType)
            .IsUnique()
            .HasFilter("[SystemAccountType] IS NOT NULL");

        builder.HasOne(a => a.ParentAccount)
            .WithMany(p => p.SubAccounts)
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
