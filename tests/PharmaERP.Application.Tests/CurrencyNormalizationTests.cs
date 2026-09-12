using System;
using System.Globalization;
using PharmaERP.Application.Common.Helpers;
using PharmaERP.Application.DTOs;
using Xunit;

namespace PharmaERP.Application.Tests;

/// <summary>
/// Tests that directly verify the production CurrencyNormalizationHelper logic:
/// decimal-place-aware rounding, elimination of negative zero, and culture-aware formatting.
/// </summary>
public class CurrencyNormalizationTests
{
    // PKR (2 decimal places)

    [Fact]
    public void PKR_ExactZero_ShouldNotRenderNegative()
    {
        var result = CurrencyNormalizationHelper.Format(0m, 2, "PKR", new CultureInfo("en-PK"));
        Assert.DoesNotContain("-", result);
        Assert.Contains("0.00", result);
        Assert.Equal("PKR 0.00", result);
    }

    [Fact]
    public void PKR_TinyNegative_SmallerThanHalfCent_ShouldRoundToZero()
    {
        // -0.004 rounds to 0.00 at 2dp — must NOT render as "-0.00"
        var result = CurrencyNormalizationHelper.Format(-0.004m, 2, "PKR", new CultureInfo("en-PK"));
        Assert.DoesNotContain("-", result);
        Assert.Contains("0.00", result);
        Assert.Equal("PKR 0.00", result);
    }

    [Fact]
    public void PKR_TinyNegative_MinusEpsilon_ShouldRoundToZero()
    {
        var result = CurrencyNormalizationHelper.Format(-0.000001m, 2, "PKR", new CultureInfo("en-PK"));
        Assert.DoesNotContain("-", result);
        Assert.Contains("0.00", result);
        Assert.Equal("PKR 0.00", result);
    }

    [Fact]
    public void PKR_NegativeAmount_LargerThanRoundingThreshold_ShouldStayNegative()
    {
        // -0.005 rounds away from zero to -0.01 at 2dp
        var result = CurrencyNormalizationHelper.Format(-0.005m, 2, "PKR", new CultureInfo("en-PK"));
        Assert.Contains("-", result);
        Assert.Equal("PKR -0.01", result);
    }

    [Fact]
    public void PKR_PositiveAmount_ShouldFormatCorrectly()
    {
        var result = CurrencyNormalizationHelper.Format(1234.567m, 2, "PKR", new CultureInfo("en-PK"));
        Assert.Contains("1,234.57", result);
        Assert.DoesNotContain("-", result);
    }

    // USD (2 decimal places)

    [Fact]
    public void USD_TinyNegative_ShouldRoundToZero()
    {
        var result = CurrencyNormalizationHelper.Format(-0.0049m, 2, "$", new CultureInfo("en-US"));
        Assert.DoesNotContain("-", result);
        Assert.Contains("0.00", result);
        Assert.Equal("$ 0.00", result);
    }

    [Fact]
    public void USD_NegativeAmount_ShouldRenderNegative()
    {
        var result = CurrencyNormalizationHelper.Format(-100.00m, 2, "$", new CultureInfo("en-US"));
        Assert.Contains("100.00", result);
        Assert.Contains("-", result);
        Assert.Equal("$ -100.00", result);
    }

    // JPY (0 decimal places)

    [Fact]
    public void JPY_TinyNegative_ShouldRoundToZero()
    {
        // -0.4 rounds to 0 at 0dp — must NOT render as "-0"
        var result = CurrencyNormalizationHelper.Format(-0.4m, 0, "JPY", new CultureInfo("en-US"));
        Assert.DoesNotContain("-", result);
        Assert.Equal("JPY 0", result);
    }

    [Fact]
    public void JPY_NegativeHalf_ShouldRoundAwayFromZero()
    {
        // -0.5 rounds away from zero to -1 at 0dp
        var result = CurrencyNormalizationHelper.Format(-0.5m, 0, "JPY", new CultureInfo("en-US"));
        Assert.Contains("-1", result);
        Assert.Equal("JPY -1", result);
    }

    [Fact]
    public void JPY_PositiveAmount_ShouldFormatAsWholeNumber()
    {
        var result = CurrencyNormalizationHelper.Format(4500m, 0, "JPY", new CultureInfo("en-US"));
        Assert.Contains("4,500", result);
        Assert.Equal("JPY 4,500", result);
    }

    // KWD (3 decimal places)

    [Fact]
    public void KWD_TinyNegative_BelowHalfFilsAt3dp_ShouldRoundToZero()
    {
        // -0.0004 rounds to 0.000 at 3dp — must NOT render as "-0.000"
        var result = CurrencyNormalizationHelper.Format(-0.0004m, 3, "KD", new CultureInfo("en-US"));
        Assert.DoesNotContain("-", result);
        Assert.Equal("KD 0.000", result);
    }

    [Fact]
    public void KWD_ExactHalfFilsThreshold_ShouldRoundAwayFromZero()
    {
        // -0.0005 at 3dp rounds away from zero to -0.001
        var result = CurrencyNormalizationHelper.Format(-0.0005m, 3, "KD", new CultureInfo("en-US"));
        Assert.Contains("-", result);
        Assert.Equal("KD -0.001", result);
    }

    [Fact]
    public void KWD_PositiveAmount_ShouldRenderThreeDecimals()
    {
        var result = CurrencyNormalizationHelper.Format(10.5m, 3, "KD", new CultureInfo("en-US"));
        Assert.Contains("10.500", result);
        Assert.DoesNotContain("-", result);
        Assert.Equal("KD 10.500", result);
    }

    // RegionalSettingsDto overload test

    [Fact]
    public void Format_UsingRegionalSettingsDto_ProducesExpectedResult()
    {
        var settings = new RegionalSettingsDto
        {
            CurrencyCode = "PKR",
            CurrencySymbol = "Rs.",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-PK"
        };

        var result = CurrencyNormalizationHelper.Format(-0.002m, settings);
        Assert.Equal("Rs. 0.00", result);
    }

    // General contract: tiny negatives below precision never produce negative zero display

    [Theory]
    [InlineData(-0.004, 2, "PKR")]
    [InlineData(-0.000001, 2, "USD")]
    [InlineData(-0.4, 0, "JPY")]
    [InlineData(-0.0004, 3, "KWD")]
    public void AllCurrencies_TinyNegativeBelowPrecision_NeverRenderNegativeZero(
        double rawAmount, int decimalPlaces, string symbol)
    {
        var result = CurrencyNormalizationHelper.Format((decimal)rawAmount, decimalPlaces, symbol, new CultureInfo("en-US"));
        string numberPart = result.Substring(symbol.Length + 1);
        bool parsed = decimal.TryParse(
            numberPart,
            NumberStyles.Number,
            CultureInfo.GetCultureInfo("en-US"),
            out var parsed2);
        if (parsed && parsed2 == 0m)
        {
            Assert.DoesNotContain("-", result);
        }
    }
}

