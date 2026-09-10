using PharmaERP.Application.DTOs;

namespace PharmaERP.Desktop.Services;

/// <summary>
/// Ambient static access to the active application currency formatter.
/// Allows WPF converters, document builders, and ViewModels to format monetary values without hardcoding currency symbols.
/// </summary>
public static class AppCurrency
{
    private static ICurrencyFormatter _instance = new CurrencyFormatter();

    public static void Initialize(ICurrencyFormatter formatter)
    {
        _instance = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public static string Code => _instance.CurrencyCode;
    public static string Symbol => _instance.CurrencySymbol;
    public static int DecimalPlaces => _instance.DecimalPlaces;
    public static string CultureName => _instance.CultureName;

    public static string Format(decimal amount, bool includeSymbol = true) => _instance.Format(amount, includeSymbol);
    public static string Format(decimal? amount, bool includeSymbol = true) => _instance.Format(amount, includeSymbol);

    public static void UpdateSettings(RegionalSettingsDto settings) => _instance.UpdateSettings(settings);

    public static event EventHandler? CurrencyChanged
    {
        add => _instance.CurrencyChanged += value;
        remove => _instance.CurrencyChanged -= value;
    }
}

