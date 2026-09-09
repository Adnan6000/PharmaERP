using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class UnitConfiguration : BaseEntityConfiguration<Unit>
{
    public override void Configure(EntityTypeBuilder<Unit> builder)
    {
        base.Configure(builder);

        builder.ToTable("Units");

        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.Abbreviation)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(u => u.Abbreviation)
            .IsUnique();
    }
}

