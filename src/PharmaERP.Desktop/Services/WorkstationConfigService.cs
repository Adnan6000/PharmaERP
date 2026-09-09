using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PharmaERP.Desktop.Models;

namespace PharmaERP.Desktop.Services;

public class WorkstationConfigService
{
    private readonly ILogger<WorkstationConfigService> _logger;
    private readonly string _configFilePath;
    private WorkstationConfig? _cachedConfig;

    public WorkstationConfigService(ILogger<WorkstationConfigService> logger)
    {
        _logger = logger;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(appData, "PharmaERP");
        Directory.CreateDirectory(folder);
        _configFilePath = Path.Combine(folder, "workstation.json");
    }

    public WorkstationConfig GetConfig()
    {
        if (_cachedConfig != null) return _cachedConfig;

        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                _cachedConfig = JsonSerializer.Deserialize<WorkstationConfig>(json) ?? new WorkstationConfig();
            }
            else
            {
                _cachedConfig = new WorkstationConfig();
                SaveConfig(_cachedConfig);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load workstation config from {Path}. Using defaults.", _configFilePath);
            _cachedConfig = new WorkstationConfig();
        }

        return _cachedConfig;
    }

    public void SaveConfig(WorkstationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _cachedConfig = config;

        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save workstation config to {Path}", _configFilePath);
        }
    }
}

