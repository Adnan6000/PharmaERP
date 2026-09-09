using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Infrastructure.Persistence.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequences");

        builder.HasKey(s => s.SequenceKey);

        builder.Property(s => s.SequenceKey)
            .HasMaxLength(50);

        builder.Property(s => s.RowVersion)
            .IsRowVersion();
    }
}

