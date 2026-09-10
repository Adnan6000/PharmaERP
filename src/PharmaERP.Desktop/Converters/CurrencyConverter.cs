using System.Globalization;
using System.Windows.Data;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.Converters;

/// <summary>
/// Converts numeric amounts into localized company currency formatted strings (e.g. 'Rs. 1,250.00').
/// </summary>
public class CurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string paramStr = parameter as string ?? string.Empty;

        if (string.Equals(paramStr, "SymbolOnly", StringComparison.OrdinalIgnoreCase))
        {
            return AppCurrency.Symbol;
        }

        if (value == null)
            return AppCurrency.Format(0m);

        decimal amount;
        if (value is decimal d)
            amount = d;
        else if (value is double dbl)
            amount = (decimal)dbl;
        else if (value is float flt)
            amount = (decimal)flt;
        else if (value is int i)
            amount = i;
        else if (value is long l)
            amount = l;
        else if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            amount = parsed;
        else
            return value.ToString() ?? string.Empty;

        bool includeSymbol = !string.Equals(paramStr, "NoSymbol", StringComparison.OrdinalIgnoreCase);
        return AppCurrency.Format(amount, includeSymbol);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            // Strip any known currency symbols or non-numeric characters except decimals/minus
            string clean = s.Replace(AppCurrency.Symbol, string.Empty).Trim();
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.CurrentCulture, out var result))
                return result;
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var resultInv))
                return resultInv;
        }
        return 0m;
    }
}

