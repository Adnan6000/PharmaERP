using System;
using System.Globalization;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Helpers;

/// <summary>
/// Canonical currency normalization and formatting helper.
/// Provides decimal-place-aware rounding (MidpointRounding.AwayFromZero) and eliminates negative zero display.
/// </summary>
public static class CurrencyNormalizationHelper
{
    /// <summary>
    /// Normalizes a monetary amount according to the configured decimal places,
    /// avoiding floating/decimal negative-zero representation.
    /// </summary>
    public static decimal Normalize(decimal amount, int decimalPlaces)
    {
        int dec = Math.Max(0, decimalPlaces);
        decimal normalized = Math.Round(amount, dec, MidpointRounding.AwayFromZero);
        if (normalized == 0m)
        {
            normalized = 0m;
        }
        return normalized;
    }

    /// <summary>
    /// Formats a monetary amount into a clean display string using the provided currency and culture parameters.
    /// </summary>
    public static string Format(decimal amount, int decimalPlaces, string? symbol, CultureInfo? cultureInfo, bool includeSymbol = true)
    {
        int dec = Math.Max(0, decimalPlaces);
        decimal normalized = Normalize(amount, dec);
        var culture = cultureInfo ?? CultureInfo.InvariantCulture;

        string numberPart = normalized.ToString($"N{dec}", culture);
        if (!includeSymbol)
        {
            return numberPart;
        }

        string sym = string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim();
        return string.IsNullOrEmpty(sym) ? numberPart : $"{sym} {numberPart}";
    }

    /// <summary>
    /// Formats a monetary amount using the supplied RegionalSettingsDto.
    /// </summary>
    public static string Format(decimal amount, RegionalSettingsDto settings, bool includeSymbol = true)
    {
        ArgumentNullException.ThrowIfNull(settings);

        CultureInfo culture;
        try
        {
            culture = new CultureInfo(string.IsNullOrWhiteSpace(settings.CultureName) ? "en-PK" : settings.CultureName);
        }
        catch
        {
            culture = CultureInfo.InvariantCulture;
        }

        string symbol = string.IsNullOrWhiteSpace(settings.CurrencySymbol) ? settings.CurrencyCode : settings.CurrencySymbol;
        return Format(amount, settings.CurrencyDecimalPlaces, symbol, culture, includeSymbol);
    }
}

