using System.Globalization;
using System.Windows;

namespace PharmaERP.Desktop.Services;

public record LanguageOptionDto(string Code, string EnglishName, string NativeName, FlowDirection Direction);

public interface ILanguageService
{
    string CurrentLanguageCode { get; }
    CultureInfo CurrentCulture { get; }
    FlowDirection CurrentFlowDirection { get; }
    IReadOnlyList<LanguageOptionDto> SupportedLanguages { get; }

    void SetLanguage(string languageCode);
    string GetString(string key);

    event EventHandler? LanguageChanged;
}

