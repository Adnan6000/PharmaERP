using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PharmaERP.Desktop.Converters;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }

        return false;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isInverted = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);

        bool isNullOrEmpty = value switch
        {
            null => true,
            string str => string.IsNullOrWhiteSpace(str),
            _ => false
        };

        if (isInverted)
        {
            return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class ConnectionStatusToBrushConverter : IValueConverter
{
    public Brush ConnectedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)); // #16A34A Green
    public Brush DisconnectedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)); // #DC2626 Red
    public Brush WarningBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)); // #D97706 Amber
    public Brush DefaultBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)); // #94A3B8 Slate Muted

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? ConnectedBrush : DisconnectedBrush;
        }

        if (value is string status)
        {
            return status.ToLowerInvariant() switch
            {
                "connected" or "online" => ConnectedBrush,
                "disconnected" or "unreachable" or "error" => DisconnectedBrush,
                "connecting..." or "checking..." or "uninitialized" => WarningBrush,
                _ => DefaultBrush
            };
        }

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

