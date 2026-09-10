using System.Globalization;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Desktop.Services;

public interface ICurrencyFormatter
{
    string CurrencyCode { get; }
    string CurrencySymbol { get; }
    int DecimalPlaces { get; }
    string CultureName { get; }

    string Format(decimal amount, bool includeSymbol = true);
    string Format(decimal? amount, bool includeSymbol = true);
    void UpdateSettings(RegionalSettingsDto settings);

    event EventHandler? CurrencyChanged;
}

public class CurrencyFormatter : ICurrencyFormatter
{
    private RegionalSettingsDto _settings = new();
    private CultureInfo _cultureInfo = new("en-PK");

    public event EventHandler? CurrencyChanged;

    public CurrencyFormatter()
    {
        UpdateSettings(new RegionalSettingsDto());
    }

    public string CurrencyCode => _settings.CurrencyCode;
    public string CurrencySymbol => _settings.CurrencySymbol;
    public int DecimalPlaces => _settings.CurrencyDecimalPlaces;
    public string CultureName => _settings.CultureName;

    public void UpdateSettings(RegionalSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        try
        {
            _cultureInfo = new CultureInfo(string.IsNullOrWhiteSpace(settings.CultureName) ? "en-PK" : settings.CultureName);
        }
        catch
        {
            _cultureInfo = CultureInfo.InvariantCulture;
        }

        CurrencyChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Format(decimal amount, bool includeSymbol = true)
    {
        int dec = Math.Max(0, _settings.CurrencyDecimalPlaces);
        string numberPart = amount.ToString($"N{dec}", _cultureInfo);

        if (!includeSymbol)
            return numberPart;

        string symbol = string.IsNullOrWhiteSpace(_settings.CurrencySymbol) ? _settings.CurrencyCode : _settings.CurrencySymbol;
        return $"{symbol} {numberPart}";
    }

    public string Format(decimal? amount, bool includeSymbol = true)
    {
        return amount.HasValue ? Format(amount.Value, includeSymbol) : Format(0m, includeSymbol);
    }
}

