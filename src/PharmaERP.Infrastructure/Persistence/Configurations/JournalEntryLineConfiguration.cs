using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class JournalEntryLineConfiguration : BaseEntityConfiguration<JournalEntryLine>
{
    public override void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        base.Configure(builder);

        builder.ToTable("JournalEntryLines", t =>
        {
            t.HasCheckConstraint("CK_JournalEntryLine_PartyMutex", "[CustomerId] IS NULL OR [SupplierId] IS NULL");
            t.HasCheckConstraint("CK_JournalEntryLine_Amounts", "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)");
            t.HasTrigger("TR_JournalEntryLines_PreventPostedModifications");
        });

        builder.Property(l => l.DebitAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.CreditAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.Narration)
            .HasMaxLength(500);

        builder.HasIndex(l => new { l.AccountId, l.JournalEntryId })
            .IncludeProperties(l => new { l.DebitAmount, l.CreditAmount });

        builder.HasIndex(l => new { l.CustomerId, l.AccountId, l.JournalEntryId })
            .HasFilter("[CustomerId] IS NOT NULL");

        builder.HasIndex(l => new { l.SupplierId, l.AccountId, l.JournalEntryId })
            .HasFilter("[SupplierId] IS NOT NULL");

        builder.HasOne(l => l.Account)
            .WithMany(a => a.JournalLines)
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Customer)
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Supplier)
            .WithMany()
            .HasForeignKey(l => l.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
