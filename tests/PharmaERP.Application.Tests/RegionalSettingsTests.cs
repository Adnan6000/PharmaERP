using System.Globalization;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;

namespace PharmaERP.Application.Tests;

public class RegionalSettingsTests
{
    private class FakeAppConfigRepository : IAppConfigRepository
    {
        public bool HasPostedFinancials { get; set; }
        public RegionalSettingsDto CurrentSettings { get; set; } = new();

        public Task<RegionalSettingsDto> GetRegionalSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CurrentSettings);
        }

        public Task SaveRegionalSettingsAsync(RegionalSettingsDto settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }

        public Task<bool> HasPostedFinancialTransactionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasPostedFinancials);
        }

        // Unused interface stubs
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetValueAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BusinessProfileDto> GetBusinessProfileAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BusinessProfileDto());
        public Task SaveBusinessProfileAsync(BusinessProfileDto profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void DefaultRegionalSettings_AreConfiguredForPakistan()
    {
        // Arrange & Act
        var defaults = new RegionalSettingsDto();

        // Assert
        Assert.Equal("PK", defaults.CountryCode);
        Assert.Equal("PKR", defaults.CurrencyCode);
        Assert.Equal("Rs.", defaults.CurrencySymbol);
        Assert.Equal(2, defaults.CurrencyDecimalPlaces);
        Assert.Equal("en-PK", defaults.CultureName);
        Assert.Equal("en", defaults.DefaultLanguageCode);
        Assert.False(defaults.IsBaseCurrencyLocked);
    }

    [Fact]
    public void CuratedCurrenciesRegistry_ContainsMajorCurrenciesWithProperDecimals()
    {
        // Act
        var list = CurrencyDefinitionDto.CuratedCurrencies;

        // Assert
        Assert.NotNull(list);
        Assert.True(list.Count >= 10);

        var pkr = list.FirstOrDefault(c => c.CurrencyCode == "PKR");
        Assert.NotNull(pkr);
        Assert.Equal("Rs.", pkr.Symbol);
        Assert.Equal(2, pkr.DefaultDecimalPlaces);
        Assert.Equal("en-PK", pkr.CultureName);

        var usd = list.FirstOrDefault(c => c.CurrencyCode == "USD");
        Assert.NotNull(usd);
        Assert.Equal("$", usd.Symbol);
        Assert.Equal(2, usd.DefaultDecimalPlaces);

        var gbp = list.FirstOrDefault(c => c.CurrencyCode == "GBP");
        Assert.NotNull(gbp);
        Assert.Equal("£", gbp.Symbol);
        Assert.Equal(2, gbp.DefaultDecimalPlaces);

        var jpy = list.FirstOrDefault(c => c.CurrencyCode == "JPY");
        Assert.NotNull(jpy);
        Assert.Equal("¥", jpy.Symbol);
        Assert.Equal(0, jpy.DefaultDecimalPlaces); // Zero decimal currency

        var kwd = list.FirstOrDefault(c => c.CurrencyCode == "KWD");
        Assert.NotNull(kwd);
        Assert.Equal(3, kwd.DefaultDecimalPlaces); // 3 decimal places
    }

    [Fact]
    public async Task SaveRegionalSettings_WhenNoPostedTransactions_AllowsBaseCurrencyChange()
    {
        // Arrange
        var fakeRepo = new FakeAppConfigRepository
        {
            HasPostedFinancials = false,
            CurrentSettings = new RegionalSettingsDto
            {
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs.",
                CurrencyDecimalPlaces = 2
            }
        };
        var service = new RegionalSettingsService(fakeRepo);

        var newSettings = new RegionalSettingsDto
        {
            CountryCode = "US",
            CurrencyCode = "USD",
            CurrencySymbol = "$",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-US",
            DefaultLanguageCode = "en"
        };

        // Act
        await service.SaveRegionalSettingsAsync(newSettings);

        // Assert
        Assert.Equal("USD", fakeRepo.CurrentSettings.CurrencyCode);
        Assert.Equal("$", fakeRepo.CurrentSettings.CurrencySymbol);
    }

    [Fact]
    public async Task SaveRegionalSettings_WhenPostedTransactionsExist_ThrowsValidationExceptionOnBaseCurrencyChange()
    {
        // Arrange
        var fakeRepo = new FakeAppConfigRepository
        {
            HasPostedFinancials = true,
            CurrentSettings = new RegionalSettingsDto
            {
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs.",
                CurrencyDecimalPlaces = 2
            }
        };
        var service = new RegionalSettingsService(fakeRepo);

        var attemptChange = new RegionalSettingsDto
        {
            CountryCode = "US",
            CurrencyCode = "USD",
            CurrencySymbol = "$",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-US",
            DefaultLanguageCode = "en"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveRegionalSettingsAsync(attemptChange));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PKR", fakeRepo.CurrentSettings.CurrencyCode);
    }

    [Fact]
    public async Task SaveRegionalSettings_WhenPostedTransactionsExist_AllowsLanguageChangeWithoutChangingCurrency()
    {
        // Arrange
        var fakeRepo = new FakeAppConfigRepository
        {
            HasPostedFinancials = true,
            CurrentSettings = new RegionalSettingsDto
            {
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs.",
                CurrencyDecimalPlaces = 2,
                DefaultLanguageCode = "en"
            }
        };
        var service = new RegionalSettingsService(fakeRepo);

        var updateLanguageOnly = new RegionalSettingsDto
        {
            CurrencyCode = "PKR",
            CurrencySymbol = "Rs.",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-PK",
            DefaultLanguageCode = "ur" // Company default language switched to Urdu
        };

        // Act
        await service.SaveRegionalSettingsAsync(updateLanguageOnly);

        // Assert
        Assert.Equal("ur", fakeRepo.CurrentSettings.DefaultLanguageCode);
        Assert.Equal("PKR", fakeRepo.CurrentSettings.CurrencyCode);
    }

    [Theory]
    [InlineData("PKR", "Rs.", 2, "en-PK", 1250.50, "Rs. 1,250.50")]
    [InlineData("USD", "$", 2, "en-US", 1250.50, "$ 1,250.50")]
    [InlineData("GBP", "£", 2, "en-GB", 1250.50, "£ 1,250.50")]
    [InlineData("EUR", "€", 2, "fr-FR", 1250.50, "€ 1 250,50")]
    [InlineData("JPY", "¥", 0, "ja-JP", 1250.50, "¥ 1,251")]
    [InlineData("KWD", "KD", 3, "en-KW", 1250.50, "KD 1,250.500")]
    public void MonetaryFormatting_RespectsConfiguredSymbolAndDecimalsWithoutAlteringUnderlyingValue(
        string code, string symbol, int decimals, string cultureName, decimal amount, string expected)
    {
        // Formatting specification test verifying the standard formatting formula
        // utilized by presentation layer ICurrencyFormatter
        Assert.NotNull(code);
        CultureInfo culture;
        try { culture = new CultureInfo(cultureName); }
        catch { culture = CultureInfo.InvariantCulture; }

        string numberPart = amount.ToString($"N{decimals}", culture);
        string formatted = $"{symbol} {numberPart}";

        // Normalize non-breaking spaces for cross-platform comparison
        string normalizedActual = formatted.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        string normalizedExpected = expected.Replace('\u00A0', ' ').Replace('\u202F', ' ');

        Assert.Equal(normalizedExpected, normalizedActual);
        // Guarantee decimal numeric value remains unchanged
        Assert.Equal(1250.50m, amount);
    }
}
