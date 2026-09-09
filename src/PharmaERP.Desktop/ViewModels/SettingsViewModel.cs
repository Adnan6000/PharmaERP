using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IDbConnectionTester _connectionTester;
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IConfiguration _configuration;

    private readonly List<string> _profiles = ["Development", "Local", "LAN"];
    private string _selectedProfile = "Local";
    private string _profileServer = string.Empty;
    private string _profileDatabase = string.Empty;
    private string _profileDescription = string.Empty;
    private string _profileConnectionString = string.Empty;

    private bool _isTestingProfile;
    private string? _profileTestResultText;
    private bool? _profileTestSuccess;

    private bool _isSavingProfile;
    private string? _saveMessage;

    private bool _isMigrating;
    private string? _migrationStatusMessage;
    private bool? _migrationSuccess;
    private string? _migrationTechnicalDetails;

    public SettingsViewModel(
        IDbConnectionTester connectionTester,
        IDatabaseMigrator databaseMigrator,
        ConnectionStateStore connectionStore,
        IConfiguration configuration)
    {
        _connectionTester = connectionTester;
        _databaseMigrator = databaseMigrator;
        _connectionStore = connectionStore;
        _configuration = configuration;

        SelectedProfile = _connectionStore.ActiveProfile;
        LoadProfileDetails(SelectedProfile);

        TestActiveConnectionCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await _connectionStore.CheckConnectionAsync(ct),
            canExecute: _ => !_connectionStore.IsChecking);

        TestSelectedProfileCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await TestSelectedProfileAsync(ct),
            canExecute: _ => !IsTestingProfile);

        ApplyProfileCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await ApplyProfileAsync(ct),
            canExecute: _ => !IsSavingProfile);

        MigrateDatabaseCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await MigrateDatabaseAsync(ct),
            canExecute: _ => !IsMigrating);
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;

    public IReadOnlyList<string> Profiles => _profiles;

    public string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                LoadProfileDetails(value);
                ProfileTestResultText = null;
                ProfileTestSuccess = null;
                SaveMessage = null;
            }
        }
    }

    public string ProfileServer
    {
        get => _profileServer;
        private set => SetField(ref _profileServer, value);
    }

    public string ProfileDatabase
    {
        get => _profileDatabase;
        private set => SetField(ref _profileDatabase, value);
    }

    public string ProfileDescription
    {
        get => _profileDescription;
        private set => SetField(ref _profileDescription, value);
    }

    public string ProfileConnectionString
    {
        get => _profileConnectionString;
        private set => SetField(ref _profileConnectionString, value);
    }

    public bool IsTestingProfile
    {
        get => _isTestingProfile;
        private set => SetField(ref _isTestingProfile, value);
    }

    public string? ProfileTestResultText
    {
        get => _profileTestResultText;
        private set => SetField(ref _profileTestResultText, value);
    }

    public bool? ProfileTestSuccess
    {
        get => _profileTestSuccess;
        private set => SetField(ref _profileTestSuccess, value);
    }

    public bool IsSavingProfile
    {
        get => _isSavingProfile;
        private set => SetField(ref _isSavingProfile, value);
    }

    public string? SaveMessage
    {
        get => _saveMessage;
        private set => SetField(ref _saveMessage, value);
    }

    public bool IsMigrating
    {
        get => _isMigrating;
        private set => SetField(ref _isMigrating, value);
    }

    public string? MigrationStatusMessage
    {
        get => _migrationStatusMessage;
        private set => SetField(ref _migrationStatusMessage, value);
    }

    public bool? MigrationSuccess
    {
        get => _migrationSuccess;
        private set => SetField(ref _migrationSuccess, value);
    }

    public string? MigrationTechnicalDetails
    {
        get => _migrationTechnicalDetails;
        private set => SetField(ref _migrationTechnicalDetails, value);
    }

    public ICommand TestActiveConnectionCommand { get; }
    public ICommand TestSelectedProfileCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand MigrateDatabaseCommand { get; }

    private void LoadProfileDetails(string profileName)
    {
        ProfileDescription = _configuration[$"DatabaseConfig:Profiles:{profileName}:Description"] ?? "Profile configuration";
        ProfileServer = _configuration[$"DatabaseConfig:Profiles:{profileName}:Server"] ?? "(default)";
        ProfileDatabase = _configuration[$"DatabaseConfig:Profiles:{profileName}:Database"] ?? "PharmaERP_Dev";
        ProfileConnectionString = _configuration[$"DatabaseConfig:Profiles:{profileName}:ConnectionString"] ?? string.Empty;
    }

    public async Task TestSelectedProfileAsync(CancellationToken ct)
    {
        IsTestingProfile = true;
        ProfileTestResultText = "Verifying profile connection...";
        ProfileTestSuccess = null;

        try
        {
            var result = await _connectionTester.TestProfileConnectionAsync(
                SelectedProfile,
                ProfileConnectionString,
                ct);

            ProfileTestSuccess = result.IsConnected;
            if (result.IsConnected)
            {
                ProfileTestResultText = $"Connection Successful! Latency: {result.Latency.TotalMilliseconds:F0} ms. {(result.IsDatabaseInitialized ? "Database schema is initialized." : "Schema migration pending.")}";
            }
            else
            {
                ProfileTestResultText = $"Connection Failed: {result.ErrorMessage ?? "Server unreachable."}";
            }
        }
        catch (Exception ex)
        {
            ProfileTestSuccess = false;
            ProfileTestResultText = $"Connection Test Error: {ex.Message}";
        }
        finally
        {
            IsTestingProfile = false;
        }
    }

    public Task ApplyProfileAsync(CancellationToken ct)
    {
        IsSavingProfile = true;
        SaveMessage = null;

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appData, "PharmaERP");
            Directory.CreateDirectory(appFolder);

            string settingsFile = Path.Combine(appFolder, "settings.json");

            var settingsDoc = new
            {
                DatabaseConfig = new
                {
                    ActiveProfile = SelectedProfile
                }
            };

            string json = JsonSerializer.Serialize(settingsDoc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsFile, json);

            SaveMessage = $"Active profile set to '{SelectedProfile}' in {settingsFile}. Please restart the application for this setting to take effect.";
        }
        catch (Exception ex)
        {
            SaveMessage = $"Failed saving profile: {ex.Message}";
        }
        finally
        {
            IsSavingProfile = false;
        }

        return Task.CompletedTask;
    }

    public async Task MigrateDatabaseAsync(CancellationToken ct)
    {
        IsMigrating = true;
        MigrationStatusMessage = "Applying database schema migrations...";
        MigrationSuccess = null;
        MigrationTechnicalDetails = null;

        try
        {
            var result = await _databaseMigrator.MigrateDatabaseAsync(ct);
            MigrationSuccess = result.Success;
            MigrationStatusMessage = result.Message;
            MigrationTechnicalDetails = result.TechnicalDetails;

            if (result.Success)
            {
                await _connectionStore.CheckConnectionAsync(ct);
            }
        }
        catch (Exception ex)
        {
            MigrationSuccess = false;
            MigrationStatusMessage = "Migration failed unexpectedly.";
            MigrationTechnicalDetails = ex.ToString();
        }
        finally
        {
            IsMigrating = false;
        }
    }
}

