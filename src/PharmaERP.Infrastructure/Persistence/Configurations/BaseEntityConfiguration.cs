using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Common;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reusable generic configuration mapping common BaseEntity properties
/// including audit timestamps and optimistic concurrency RowVersion.
/// </summary>
public abstract class BaseEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : BaseEntity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired(false);

        // Map framework-neutral byte[] RowVersion as SQL Server rowversion/concurrency token
        builder.Property(e => e.RowVersion)
            .IsRowVersion();
    }
}

