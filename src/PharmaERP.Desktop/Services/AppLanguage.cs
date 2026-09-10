using System.ComponentModel;
using System.Windows;

namespace PharmaERP.Desktop.Services;

/// <summary>
/// Ambient observable localization source for XAML data-binding and code-behind string retrieval.
/// Allows dynamic in-place language switching without application restart.
/// </summary>
public class AppLanguage : INotifyPropertyChanged
{
    private static ILanguageService? _service;
    private static readonly AppLanguage _instance = new();

    public static AppLanguage Current => _instance;

    public static void Initialize(ILanguageService service)
    {
        if (_service != null)
        {
            _service.LanguageChanged -= OnServiceLanguageChanged;
        }
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.LanguageChanged += OnServiceLanguageChanged;
        _instance.OnLanguageChanged();
    }

    private static void OnServiceLanguageChanged(object? sender, EventArgs e)
    {
        _instance.OnLanguageChanged();
    }

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get
        {
            if (_service == null) return key;
            return _service.GetString(key);
        }
    }

    public static string Get(string key)
    {
        if (_service == null) return key;
        return _service.GetString(key);
    }

    public static string CurrentLanguageCode => _service?.CurrentLanguageCode ?? "en";
    public static FlowDirection CurrentFlowDirection => _service?.CurrentFlowDirection ?? FlowDirection.LeftToRight;
}

