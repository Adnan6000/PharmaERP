using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;

namespace PharmaERP.Infrastructure.Persistence;

/// <summary>
/// Authoritative file-based store for workstation database settings in %LocalAppData%\PharmaERP\settings.json.
/// Guarantees canonical schema persistence and backward compatibility with legacy formats.
/// </summary>
public class WorkstationDatabaseConfigStore : IDatabaseConfigStore
{
    private readonly ILogger<WorkstationDatabaseConfigStore>? _logger;
    private readonly string _settingsFilePath;

    public WorkstationDatabaseConfigStore(ILogger<WorkstationDatabaseConfigStore>? logger = null, string? customSettingsPath = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
        }
        else
        {
            string? appData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            string folder = Path.Combine(appData, "PharmaERP");
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }
    }

    public string SettingsFilePath => _settingsFilePath;

    public bool ConfigExists()
    {
        try
        {
            if (!File.Exists(_settingsFilePath)) return false;
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json)) return false;

            using var doc = JsonDocument.Parse(json);
            // Must have either DatabaseConfig or ActiveProfile
            return doc.RootElement.TryGetProperty("DatabaseConfig", out _) ||
                   doc.RootElement.TryGetProperty("ActiveProfile", out _);
        }
        catch
        {
            return false;
        }
    }

    public DatabaseConnectionConfig? LoadConfig()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var config = new DatabaseConnectionConfig();

            // 1. Check if canonical DatabaseConfig object is present
            if (root.TryGetProperty("DatabaseConfig", out var dbConfigElement) && dbConfigElement.ValueKind == JsonValueKind.Object)
            {
                if (dbConfigElement.TryGetProperty("Server", out var s) && s.ValueKind == JsonValueKind.String)
                    config.Server = s.GetString() ?? string.Empty;

                if (dbConfigElement.TryGetProperty("Database", out var d) && d.ValueKind == JsonValueKind.String)
                    config.Database = d.GetString() ?? "PharmaERP";

                if (dbConfigElement.TryGetProperty("AuthType", out var at))
                {
                    if (at.ValueKind == JsonValueKind.Number && Enum.IsDefined(typeof(DatabaseAuthType), at.GetInt32()))
                        config.AuthType = (DatabaseAuthType)at.GetInt32();
                    else if (at.ValueKind == JsonValueKind.String && Enum.TryParse<DatabaseAuthType>(at.GetString(), true, out var parsedAuth))
                        config.AuthType = parsedAuth;
                }

                if (dbConfigElement.TryGetProperty("UserName", out var u) && u.ValueKind == JsonValueKind.String)
                    config.UserName = u.GetString();

                if (dbConfigElement.TryGetProperty("EncryptedPassword", out var p) && p.ValueKind == JsonValueKind.String)
                    config.EncryptedPassword = p.GetString();

                if (dbConfigElement.TryGetProperty("ActiveProfile", out var ap) && ap.ValueKind == JsonValueKind.String)
                    config.ActiveProfile = ap.GetString() ?? "Custom";

                if (dbConfigElement.TryGetProperty("Encrypt", out var enc) && (enc.ValueKind == JsonValueKind.True || enc.ValueKind == JsonValueKind.False))
                    config.Encrypt = enc.GetBoolean();

                if (dbConfigElement.TryGetProperty("TrustServerCertificate", out var tsc) && (tsc.ValueKind == JsonValueKind.True || tsc.ValueKind == JsonValueKind.False))
                    config.TrustServerCertificate = tsc.GetBoolean();

                if (dbConfigElement.TryGetProperty("ConnectionTimeoutSeconds", out var ct) && ct.ValueKind == JsonValueKind.Number)
                    config.ConnectionTimeoutSeconds = ct.GetInt32();

                if (dbConfigElement.TryGetProperty("Description", out var desc) && desc.ValueKind == JsonValueKind.String)
                    config.Description = desc.GetString();
            }

            // 2. Handle legacy / profile-based mapping if Server is not explicitly set
            string? profileToMap = null;
            if (root.TryGetProperty("ActiveProfile", out var rootAp) && rootAp.ValueKind == JsonValueKind.String)
            {
                profileToMap = rootAp.GetString();
            }
            else if (!string.IsNullOrWhiteSpace(config.ActiveProfile) && !config.ActiveProfile.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                profileToMap = config.ActiveProfile;
            }

            if (string.IsNullOrWhiteSpace(config.Server) && !string.IsNullOrWhiteSpace(profileToMap))
            {
                config.ActiveProfile = profileToMap;
                switch (profileToMap.ToUpperInvariant())
                {
                    case "DEVELOPMENT":
                        config.Server = "(localdb)\\mssqllocaldb";
                        config.Database = "PharmaERP_Dev";
                        config.AuthType = DatabaseAuthType.Windows;
                        config.TrustServerCertificate = true;
                        break;
                    case "LAN":
                        config.Server = "192.168.1.100,1433";
                        config.Database = "PharmaERP";
                        config.AuthType = DatabaseAuthType.Windows;
                        config.TrustServerCertificate = true;
                        break;
                    case "LOCAL":
                    default:
                        config.Server = ".\\SQLEXPRESS";
                        config.Database = "PharmaERP";
                        config.AuthType = DatabaseAuthType.Windows;
                        config.TrustServerCertificate = true;
                        break;
                }
            }

            return config.IsConfigured ? config : null;
        }
        catch (Exception ex)
        {
            if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Failed to parse database configuration from {Path}", _settingsFilePath);
            }
            return null;
        }
    }

    public void SaveConfig(DatabaseConnectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        string? dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var root = new JsonObject
        {
            ["ActiveProfile"] = config.ActiveProfile,
            ["DatabaseConfig"] = new JsonObject
            {
                ["ActiveProfile"] = config.ActiveProfile,
                ["Server"] = config.Server,
                ["Database"] = config.Database,
                ["AuthType"] = config.AuthType.ToString(),
                ["UserName"] = config.UserName ?? string.Empty,
                ["EncryptedPassword"] = config.EncryptedPassword ?? string.Empty,
                ["Encrypt"] = config.Encrypt,
                ["TrustServerCertificate"] = config.TrustServerCertificate,
                ["ConnectionTimeoutSeconds"] = config.ConnectionTimeoutSeconds,
                ["Description"] = config.Description ?? string.Empty
            }
        };

        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Saved canonical database configuration to {Path}", _settingsFilePath);
        }
    }
}

