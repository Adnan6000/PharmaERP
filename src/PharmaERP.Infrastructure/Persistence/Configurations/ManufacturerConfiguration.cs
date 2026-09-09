using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class ManufacturerConfiguration : BaseEntityConfiguration<Manufacturer>
{
    public override void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        base.Configure(builder);

        builder.ToTable("Manufacturers");

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(m => m.ContactPerson)
            .HasMaxLength(100);

        builder.Property(m => m.Phone)
            .HasMaxLength(30);

        builder.Property(m => m.Email)
            .HasMaxLength(100);

        builder.Property(m => m.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(m => m.Name);
    }
}

