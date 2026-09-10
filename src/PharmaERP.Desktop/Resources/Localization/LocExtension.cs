using System.Windows.Data;
using System.Windows.Markup;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.Resources.Localization;

[ContentProperty("Key")]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        // Create dynamic binding to AppLanguage.Current[Key]
        var binding = new Binding($"[{Key}]")
        {
            Source = AppLanguage.Current,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}

