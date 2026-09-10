using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class JournalEntryConfiguration : BaseEntityConfiguration<JournalEntry>
{
    public override void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        base.Configure(builder);

        builder.ToTable("JournalEntries", tb =>
        {
            tb.HasTrigger("TR_JournalEntries_EnforceBalanceOnPost");
            tb.HasTrigger("TR_JournalEntries_PreventPostedHeaderModifications");
        });

        builder.Property(j => j.EntryNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(j => j.BusinessDate)
            .IsRequired();

        builder.Property(j => j.PostedAtUtc)
            .IsRequired();

        builder.Property(j => j.Narration)
            .HasMaxLength(500);

        builder.Property(j => j.SourceDocumentType)
            .IsRequired();

        builder.Property(j => j.SourceDocumentNumber)
            .HasMaxLength(50);

        builder.Property(j => j.PostingRole)
            .IsRequired();

        builder.Property(j => j.Status)
            .IsRequired();

        builder.Property(j => j.OperationId)
            .IsRequired();

        builder.Property(j => j.TotalDebit)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(j => j.TotalCredit)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(j => j.CancellationReason)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(j => j.EntryNumber)
            .IsUnique();

        builder.HasIndex(j => j.OperationId)
            .IsUnique();

        builder.HasIndex(j => new { j.SourceDocumentType, j.SourceDocumentId, j.PostingRole })
            .IsUnique()
            .HasFilter("[SourceDocumentId] IS NOT NULL");

        builder.HasIndex(j => j.ReversesJournalEntryId)
            .IsUnique()
            .HasFilter("[ReversesJournalEntryId] IS NOT NULL");

        builder.HasIndex(j => new { j.BusinessDate, j.PostedAtUtc, j.Id });

        builder.HasOne(j => j.ReversesJournalEntry)
            .WithMany()
            .HasForeignKey(j => j.ReversesJournalEntryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(j => j.Lines)
            .WithOne(l => l.JournalEntry)
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
