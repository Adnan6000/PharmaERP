using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.QuantityDelta)
            .HasPrecision(18, 4);

        builder.Property(m => m.BalanceAfter)
            .HasPrecision(18, 4);

        builder.Property(m => m.UnitCost)
            .HasPrecision(18, 4);

        builder.Property(m => m.InventoryCostRate)
            .HasPrecision(18, 4);

        builder.Property(m => m.InventoryValueDelta)
            .HasPrecision(18, 4);

        builder.Property(m => m.InventoryValueAfter)
            .HasPrecision(18, 4);

        builder.Property(m => m.ReferenceDocumentNumber)
            .HasMaxLength(50);

        builder.Property(m => m.Remarks)
            .HasMaxLength(250);

        builder.HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.ProductBatch)
            .WithMany(b => b.StockMovements)
            .HasForeignKey(m => m.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing reversal tracking: restrictive delete behavior
        builder.HasOne(m => m.ReversesStockMovement)
            .WithMany()
            .HasForeignKey(m => m.ReversesStockMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        // Query-driven indexes for audit trail & batch history
        builder.HasIndex(m => new { m.ProductBatchId, m.TransactionDateUtc });
        builder.HasIndex(m => new { m.ProductId, m.TransactionDateUtc });
        builder.HasIndex(m => new { m.ReferenceDocumentType, m.ReferenceDocumentId });
    }
}

