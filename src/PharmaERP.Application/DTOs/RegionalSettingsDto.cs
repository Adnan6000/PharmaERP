namespace PharmaERP.Application.DTOs;

public class RegionalSettingsDto
{
    public string CountryCode { get; set; } = "PK";
    public string CurrencyCode { get; set; } = "PKR";
    public string CurrencySymbol { get; set; } = "Rs.";
    public int CurrencyDecimalPlaces { get; set; } = 2;
    public string CultureName { get; set; } = "en-PK";
    public string DefaultLanguageCode { get; set; } = "en";

    /// <summary>
    /// True if the company database already contains posted financial transactions (e.g. posted GL journals).
    /// When locked, base currency cannot be modified.
    /// </summary>
    public bool IsBaseCurrencyLocked { get; set; }
}

public record CurrencyDefinitionDto(
    string CurrencyCode,
    string CurrencyName,
    string Symbol,
    int DefaultDecimalPlaces,
    string CountryCode,
    string CultureName)
{
    public string DisplayName => $"{CurrencyCode} — {CurrencyName} ({Symbol})";

    public override string ToString() => DisplayName;

    public static readonly IReadOnlyList<CurrencyDefinitionDto> CuratedCurrencies = new List<CurrencyDefinitionDto>
    {
        new("PKR", "Pakistani Rupee", "Rs.", 2, "PK", "en-PK"),
        new("USD", "US Dollar", "$", 2, "US", "en-US"),
        new("GBP", "British Pound", "£", 2, "GB", "en-GB"),
        new("EUR", "Euro", "€", 2, "EU", "en-IE"),
        new("AED", "UAE Dirham", "AED", 2, "AE", "en-AE"),
        new("SAR", "Saudi Riyal", "SAR", 2, "SA", "en-SA"),
        new("CAD", "Canadian Dollar", "C$", 2, "CA", "en-CA"),
        new("AUD", "Australian Dollar", "A$", 2, "AU", "en-AU"),
        new("INR", "Indian Rupee", "₹", 2, "IN", "en-IN"),
        new("KWD", "Kuwaiti Dinar", "KD", 3, "KW", "en-KW"),
        new("JPY", "Japanese Yen", "¥", 0, "JP", "ja-JP"),
    };

    public static CurrencyDefinitionDto GetOrDefault(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return CuratedCurrencies[0]; // PKR

        var match = CuratedCurrencies.FirstOrDefault(c => string.Equals(c.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));
        return match ?? new CurrencyDefinitionDto(currencyCode.ToUpperInvariant(), currencyCode.ToUpperInvariant(), currencyCode.ToUpperInvariant(), 2, "GLOBAL", "en-US");
    }

    public static CurrencyDefinitionDto GetByCountryOrDefault(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return CuratedCurrencies[0]; // PKR

        var match = CuratedCurrencies.FirstOrDefault(c => string.Equals(c.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase));
        return match ?? CuratedCurrencies[0];
    }
}

