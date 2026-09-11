using System.Reflection;

namespace PharmaERP.Desktop.Services;

public static class AppVersionInfo
{
    public static string Version { get; } = ResolveVersion();
    public static string DisplayVersion => $"v{Version}";
    public static string FullTitle => $"PharmaERP {DisplayVersion}";

    private static string ResolveVersion()
    {
        try
        {
            var asm = typeof(AppVersionInfo).Assembly;
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                return infoVer.Split('+')[0];
            }

            var ver = asm.GetName().Version;
            if (ver != null)
            {
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch
        {
        }

        return "0.5.0-rc2";
    }
}
