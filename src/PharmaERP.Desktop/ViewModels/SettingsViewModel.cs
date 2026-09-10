using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IDbConnectionTester _connectionTester;
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IConfiguration _configuration;
    private readonly IRegionalSettingsService _regionalSettingsService;
    private readonly ICurrencyFormatter _currencyFormatter;
    private readonly ILanguageService _languageService;

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

    // Regional & Base Currency
    private RegionalSettingsDto _regionalSettings = new();
    private CurrencyDefinitionDto? _selectedCurrency;
    private readonly IDatabaseConfigStore _databaseConfigStore;
    private LanguageOptionDto? _selectedLanguage;
    private bool _isSavingRegional;
    private string? _regionalSaveMessage;

    public SettingsViewModel(
        IDbConnectionTester connectionTester,
        IDatabaseMigrator databaseMigrator,
        ConnectionStateStore connectionStore,
        IConfiguration configuration,
        IRegionalSettingsService regionalSettingsService,
        ICurrencyFormatter currencyFormatter,
        ILanguageService languageService,
        IDatabaseConfigStore databaseConfigStore)
    {
        _connectionTester = connectionTester;
        _databaseMigrator = databaseMigrator;
        _connectionStore = connectionStore;
        _configuration = configuration;
        _regionalSettingsService = regionalSettingsService;
        _currencyFormatter = currencyFormatter;
        _languageService = languageService;
        _databaseConfigStore = databaseConfigStore;

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

        SaveRegionalSettingsCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveRegionalSettingsAsync(ct),
            canExecute: _ => !IsSavingRegional);

        OpenSetupWizardCommand = new RelayCommand(_ =>
        {
            var protector = new PharmaERP.Infrastructure.Security.DpapiCredentialProtector();
            var factory = new PharmaERP.Infrastructure.Persistence.SqlConnectionStringFactory(protector);
            var setupVm = new DatabaseSetupViewModel(
                _connectionTester,
                _databaseMigrator,
                _databaseConfigStore,
                protector,
                factory);

            var window = new Views.DatabaseSetupWindow(setupVm);
            window.Owner = System.Windows.Application.Current?.MainWindow;
            window.ShowDialog();
            _ = _connectionStore.CheckConnectionAsync();
        });

        _ = LoadRegionalSettingsAsync();
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
    public ICommand SaveRegionalSettingsCommand { get; }
    public ICommand OpenSetupWizardCommand { get; }

    // Regional Properties
    public IReadOnlyList<CurrencyDefinitionDto> CuratedCurrencies => CurrencyDefinitionDto.CuratedCurrencies;
    public IReadOnlyList<LanguageOptionDto> SupportedLanguages => _languageService.SupportedLanguages;

    public CurrencyDefinitionDto? SelectedCurrency
    {
        get => _selectedCurrency;
        set
        {
            if (SetField(ref _selectedCurrency, value))
            {
                OnPropertyChanged(nameof(SampleAmountFormatted));
            }
        }
    }

    public LanguageOptionDto? SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetField(ref _selectedLanguage, value);
    }

    public string CountryCode => _regionalSettings.CountryCode;
    public string CurrencyCode => _selectedCurrency?.CurrencyCode ?? _regionalSettings.CurrencyCode;
    public string CurrencySymbol => _selectedCurrency?.Symbol ?? _regionalSettings.CurrencySymbol;
    public int CurrencyDecimalPlaces => _selectedCurrency?.DefaultDecimalPlaces ?? _regionalSettings.CurrencyDecimalPlaces;
    public string CultureName => _selectedCurrency?.CultureName ?? _regionalSettings.CultureName;
    public bool IsBaseCurrencyLocked => _regionalSettings.IsBaseCurrencyLocked;

    public string SampleAmountFormatted
    {
        get
        {
            var curr = SelectedCurrency ?? CurrencyDefinitionDto.GetOrDefault(_regionalSettings.CurrencyCode);
            string sym = curr.Symbol;
            int dec = curr.DefaultDecimalPlaces;
            decimal sample = 1250.00m;
            return $"{sym} {sample.ToString($"N{dec}", System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    public bool IsSavingRegional
    {
        get => _isSavingRegional;
        private set => SetField(ref _isSavingRegional, value);
    }

    public string? RegionalSaveMessage
    {
        get => _regionalSaveMessage;
        private set => SetField(ref _regionalSaveMessage, value);
    }

    public async Task LoadRegionalSettingsAsync()
    {
        try
        {
            _regionalSettings = await _regionalSettingsService.GetRegionalSettingsAsync();
            _selectedCurrency = CurrencyDefinitionDto.GetOrDefault(_regionalSettings.CurrencyCode);
            _selectedLanguage = _languageService.SupportedLanguages.FirstOrDefault(l => l.Code == _languageService.CurrentLanguageCode)
                                ?? _languageService.SupportedLanguages[0];

            OnPropertyChanged(nameof(CountryCode));
            OnPropertyChanged(nameof(CurrencyCode));
            OnPropertyChanged(nameof(CurrencySymbol));
            OnPropertyChanged(nameof(CurrencyDecimalPlaces));
            OnPropertyChanged(nameof(CultureName));
            OnPropertyChanged(nameof(IsBaseCurrencyLocked));
            OnPropertyChanged(nameof(SelectedCurrency));
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(SampleAmountFormatted));
        }
        catch
        {
            // Keep in-memory defaults
        }
    }

    public async Task SaveRegionalSettingsAsync(CancellationToken ct)
    {
        IsSavingRegional = true;
        RegionalSaveMessage = null;

        try
        {
            if (SelectedCurrency != null && !_regionalSettings.IsBaseCurrencyLocked)
            {
                _regionalSettings.CurrencyCode = SelectedCurrency.CurrencyCode;
                _regionalSettings.CurrencySymbol = SelectedCurrency.Symbol;
                _regionalSettings.CurrencyDecimalPlaces = SelectedCurrency.DefaultDecimalPlaces;
                _regionalSettings.CountryCode = SelectedCurrency.CountryCode;
                _regionalSettings.CultureName = SelectedCurrency.CultureName;
            }

            await _regionalSettingsService.SaveRegionalSettingsAsync(_regionalSettings, ct);
            _currencyFormatter.UpdateSettings(_regionalSettings);

            if (SelectedLanguage != null)
            {
                _languageService.SetLanguage(SelectedLanguage.Code);
            }

            RegionalSaveMessage = "Company regional settings and workstation language saved successfully.";
            await LoadRegionalSettingsAsync();
        }
        catch (Exception ex)
        {
            RegionalSaveMessage = $"Failed saving regional settings: {ex.Message}";
        }
        finally
        {
            IsSavingRegional = false;
        }
    }

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
            var config = new PharmaERP.Application.Common.Models.DatabaseConnectionConfig
            {
                ActiveProfile = SelectedProfile,
                Description = ProfileDescription
            };

            switch (SelectedProfile.ToUpperInvariant())
            {
                case "DEVELOPMENT":
                    config.Server = "(localdb)\\mssqllocaldb";
                    config.Database = "PharmaERP_Dev";
                    config.AuthType = PharmaERP.Application.Common.Models.DatabaseAuthType.Windows;
                    config.TrustServerCertificate = true;
                    break;
                case "LAN":
                    config.Server = "192.168.1.100,1433";
                    config.Database = "PharmaERP";
                    config.AuthType = PharmaERP.Application.Common.Models.DatabaseAuthType.Windows;
                    config.TrustServerCertificate = true;
                    break;
                case "LOCAL":
                default:
                    config.Server = ".\\SQLEXPRESS";
                    config.Database = "PharmaERP";
                    config.AuthType = PharmaERP.Application.Common.Models.DatabaseAuthType.Windows;
                    config.TrustServerCertificate = true;
                    break;
            }

            _databaseConfigStore.SaveConfig(config);

            SaveMessage = $"Active profile set to '{SelectedProfile}' in canonical settings. Please restart the application for this setting to take effect.";
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

