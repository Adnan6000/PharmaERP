using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class AppConfigConfiguration : IEntityTypeConfiguration<AppConfig>
{
    public void Configure(EntityTypeBuilder<AppConfig> builder)
    {
        builder.ToTable("AppConfigs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(c => c.Key)
            .IsUnique();

        builder.Property(c => c.Value)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.RowVersion)
            .IsRowVersion();
    }
}

