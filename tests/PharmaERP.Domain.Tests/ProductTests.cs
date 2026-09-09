using System.Reflection;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Domain.Tests;

public class ProductTests
{
    [Fact]
    public void Product_InitializesWithDefaultValuesAndAuditing()
    {
        // Arrange & Act
        var product = new Product
        {
            Name = "Paracetamol 500mg",
            DefaultPurchasePrice = 12.5000m,
            DefaultSalePrice = 15.0000m
        };

        // Assert
        Assert.Equal("Paracetamol 500mg", product.Name);
        Assert.Equal(12.5000m, product.DefaultPurchasePrice);
        Assert.Equal(15.0000m, product.DefaultSalePrice);
        Assert.True(product.IsActive);
        Assert.NotNull(product.RowVersion);
        Assert.True(product.CreatedAtUtc <= DateTime.UtcNow);
        Assert.Null(product.UpdatedAtUtc);
    }

    [Fact]
    public void Product_DoesNotContainBatchOrExpiryProperties()
    {
        // Architectural Constraint: Batches and expiries must remain separate from Product
        var properties = typeof(Product).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("BatchNumber", properties);
        Assert.DoesNotContain("BatchNo", properties);
        Assert.DoesNotContain("ExpiryDate", properties);
        Assert.DoesNotContain("Expiry", properties);
    }

    [Fact]
    public void Unit_InitializesCorrectlyForFuturePackagingConversions()
    {
        // Arrange & Act
        var unit = new Unit
        {
            Name = "Box",
            Abbreviation = "BOX"
        };

        // Assert
        Assert.Equal("Box", unit.Name);
        Assert.Equal("BOX", unit.Abbreviation);
        Assert.True(unit.IsActive);
        Assert.NotNull(unit.Products);
    }
}

