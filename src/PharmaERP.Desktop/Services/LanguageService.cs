using System.Globalization;
using System.Windows;
using PharmaERP.Desktop.Resources.Localization;

namespace PharmaERP.Desktop.Services;

public class LanguageService : ILanguageService
{
    private readonly WorkstationConfigService _configService;
    private string _currentLanguageCode = "en";
    private CultureInfo _currentCulture = new("en");
    private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;

    public event EventHandler? LanguageChanged;

    public static readonly IReadOnlyList<LanguageOptionDto> Languages = new List<LanguageOptionDto>
    {
        new("en", "English", "English", FlowDirection.LeftToRight),
        new("ur", "Urdu", "اردو", FlowDirection.RightToLeft)
    };

    public IReadOnlyList<LanguageOptionDto> SupportedLanguages => Languages;
    public string CurrentLanguageCode => _currentLanguageCode;
    public CultureInfo CurrentCulture => _currentCulture;
    public FlowDirection CurrentFlowDirection => _currentFlowDirection;

    public LanguageService(WorkstationConfigService configService)
    {
        _configService = configService;
        var config = _configService.GetConfig();
        string initialLang = string.IsNullOrWhiteSpace(config.SelectedLanguageCode) ? "en" : config.SelectedLanguageCode;
        ApplyLanguage(initialLang, saveToConfig: false);
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = "en";

        ApplyLanguage(languageCode, saveToConfig: true);
    }

    private void ApplyLanguage(string languageCode, bool saveToConfig)
    {
        _currentLanguageCode = languageCode.StartsWith("ur", StringComparison.OrdinalIgnoreCase) ? "ur" : "en";
        _currentCulture = new CultureInfo(_currentLanguageCode == "ur" ? "ur-PK" : "en");
        _currentFlowDirection = _currentLanguageCode == "ur" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        Thread.CurrentThread.CurrentUICulture = _currentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _currentCulture;

        if (saveToConfig)
        {
            var config = _configService.GetConfig();
            config.SelectedLanguageCode = _currentLanguageCode;
            _configService.SaveConfig(config);
        }

        // Apply FlowDirection to MainWindow if available on UI thread
        var wpfApp = System.Windows.Application.Current;
        if (wpfApp != null && wpfApp.Dispatcher != null)
        {
            if (wpfApp.Dispatcher.CheckAccess())
            {
                if (wpfApp.MainWindow != null)
                {
                    wpfApp.MainWindow.FlowDirection = _currentFlowDirection;
                }
            }
            else
            {
                wpfApp.Dispatcher.InvokeAsync(() =>
                {
                    if (wpfApp.MainWindow != null)
                    {
                        wpfApp.MainWindow.FlowDirection = _currentFlowDirection;
                    }
                });
            }
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        return Strings.Get(key, _currentCulture);
    }
}

