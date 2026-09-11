using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public enum SetupStatusSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public class DatabaseSetupViewModel : ViewModelBase
{
    private readonly IDbConnectionTester _connectionTester;
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly IDatabaseConfigStore _configStore;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IConnectionStringFactory _connectionStringFactory;

    // Server & Instance selection
    private string _server = ".\\SQLEXPRESS";
    private string _database = "PharmaERP";
    private bool _isWindowsAuth = true;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private bool _encrypt = false;
    private bool _trustServerCertificate = true;

    // First-Run Company / Regional Setup
    private string _companyName = "PharmaERP Community Pharmacy";
    private CurrencyDefinitionDto _selectedCurrency;
    private LanguageOptionDto _selectedLanguage;
    private bool _isFirstTimeSetup = true;

    // State & Status
    private bool _isTesting;
    private bool _isInitializing;
    private bool _canProceed;
    private string? _statusMessage;
    private SetupStatusSeverity _statusSeverity = SetupStatusSeverity.Info;
    private string? _technicalDetails;

    public DatabaseSetupViewModel(
        IDbConnectionTester connectionTester,
        IDatabaseMigrator databaseMigrator,
        IDatabaseConfigStore configStore,
        ICredentialProtector credentialProtector,
        IConnectionStringFactory connectionStringFactory)
    {
        _connectionTester = connectionTester;
        _databaseMigrator = databaseMigrator;
        _configStore = configStore;
        _credentialProtector = credentialProtector;
        _connectionStringFactory = connectionStringFactory;

        // Curated defaults
        _selectedCurrency = CurrencyDefinitionDto.GetOrDefault("PKR")
            ?? CurrencyDefinitionDto.CuratedCurrencies[0];
        _selectedLanguage = SupportedLanguages[0];

        // Load existing config if available
        var existing = _configStore.LoadConfig();
        if (existing != null && existing.IsConfigured)
        {
            _server = existing.Server;
            _database = existing.Database;
            _isWindowsAuth = existing.AuthType == DatabaseAuthType.Windows;
            _userName = existing.UserName ?? string.Empty;
            _password = _credentialProtector.Unprotect(existing.EncryptedPassword) ?? string.Empty;
            _encrypt = existing.Encrypt;
            _trustServerCertificate = existing.TrustServerCertificate;
        }

        TestConnectionCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await TestConnectionAsync(ct),
            canExecute: _ => !IsTesting && !IsInitializing && !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Database));

        InitializeDatabaseCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await InitializeDatabaseAsync(ct),
            canExecute: _ => !IsTesting && !IsInitializing && !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Database));

        SaveAndProceedCommand = new RelayCommand(
            execute: _ => SaveAndProceed(),
            canExecute: _ => CanProceed && !IsTesting && !IsInitializing);

        CancelCommand = new RelayCommand(
            execute: _ => RequestClose?.Invoke(false));

        UpdateStatus("Please verify your SQL Server instance and click 'Test Connection'.", SetupStatusSeverity.Info);
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<string> ServerSuggestions { get; } = new()
    {
        ".\\SQLEXPRESS",
        "localhost\\SQLEXPRESS",
        "(localdb)\\MSSQLLocalDB",
        "localhost",
        "127.0.0.1"
    };

    public IReadOnlyList<CurrencyDefinitionDto> CuratedCurrencies => CurrencyDefinitionDto.CuratedCurrencies;

    public IReadOnlyList<LanguageOptionDto> SupportedLanguages => LanguageService.Languages;

    public string Server
    {
        get => _server;
        set
        {
            if (SetField(ref _server, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public string Database
    {
        get => _database;
        set
        {
            if (SetField(ref _database, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public bool IsWindowsAuth
    {
        get => _isWindowsAuth;
        set
        {
            if (SetField(ref _isWindowsAuth, value))
            {
                OnPropertyChanged(nameof(IsSqlAuth));
                OnPropertyChanged(nameof(AuthDisplay));
                OnPropertyChanged(nameof(MaskedConnectionString));
                CanProceed = false;
            }
        }
    }

    public bool IsSqlAuth
    {
        get => !_isWindowsAuth;
        set => IsWindowsAuth = !value;
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetField(ref _userName, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(AuthDisplay));
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public bool Encrypt
    {
        get => _encrypt;
        set
        {
            if (SetField(ref _encrypt, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(SecurityDisplay));
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public bool TrustServerCertificate
    {
        get => _trustServerCertificate;
        set
        {
            if (SetField(ref _trustServerCertificate, value))
            {
                CanProceed = false;
                OnPropertyChanged(nameof(SecurityDisplay));
                OnPropertyChanged(nameof(MaskedConnectionString));
            }
        }
    }

    public string AuthDisplay => IsWindowsAuth ? "Windows Authentication" : (string.IsNullOrWhiteSpace(UserName) ? "SQL Server Authentication" : $"SQL Server Auth ({UserName})");

    public string SecurityDisplay => (TrustServerCertificate ? "Trust Certificate" : "Validate Certificate") + (Encrypt ? " (Encrypted)" : " (Unencrypted)");

    public string CompanyName
    {
        get => _companyName;
        set => SetField(ref _companyName, value);
    }

    public CurrencyDefinitionDto SelectedCurrency
    {
        get => _selectedCurrency;
        set => SetField(ref _selectedCurrency, value);
    }

    public LanguageOptionDto SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetField(ref _selectedLanguage, value);
    }

    public bool IsFirstTimeSetup
    {
        get => _isFirstTimeSetup;
        private set => SetField(ref _isFirstTimeSetup, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set => SetField(ref _isTesting, value);
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set => SetField(ref _isInitializing, value);
    }

    public bool CanProceed
    {
        get => _canProceed;
        private set => SetField(ref _canProceed, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public SetupStatusSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetField(ref _statusSeverity, value);
    }

    public string? TechnicalDetails
    {
        get => _technicalDetails;
        private set => SetField(ref _technicalDetails, value);
    }

    public string MaskedConnectionString
    {
        get
        {
            try
            {
                var config = BuildCurrentConfig();
                return _connectionStringFactory.BuildMaskedConnectionString(config);
            }
            catch
            {
                return "Configuring...";
            }
        }
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand InitializeDatabaseCommand { get; }
    public ICommand SaveAndProceedCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        IsTesting = true;
        TechnicalDetails = null;
        UpdateStatus("Connecting to SQL Server...", SetupStatusSeverity.Info);

        try
        {
            var config = BuildCurrentConfig();
            var result = await _connectionTester.TestConfigAsync(config, ct);

            if (!result.ServerReachable)
            {
                UpdateStatus(result.ErrorMessage ?? "Unable to reach SQL Server.", SetupStatusSeverity.Error);
                CanProceed = false;
                return;
            }

            if (!result.DatabaseExists)
            {
                IsFirstTimeSetup = true;
                if (result.CanCreateDatabase)
                {
                    UpdateStatus($"SQL Server reachable! Database '{Database}' does not exist yet. Click 'Create & Initialize Database' below.", SetupStatusSeverity.Warning);
                }
                else
                {
                    UpdateStatus($"SQL Server reachable, but your login lacks CREATE DATABASE permission. An administrator must create '{Database}' first.", SetupStatusSeverity.Error);
                }
                CanProceed = false;
                return;
            }

            // Database exists
            if (result.IsConnected && result.IsDatabaseInitialized)
            {
                IsFirstTimeSetup = false;
                UpdateStatus($"Connected successfully! Database '{Database}' is initialized and ready. Latency: {result.Latency.TotalMilliseconds:F0} ms.", SetupStatusSeverity.Success);
                CanProceed = true;
            }
            else if (result.IsConnected)
            {
                IsFirstTimeSetup = true;
                UpdateStatus($"Connected to database '{Database}', but schema migrations are pending ({result.PendingMigrationsCount} pending). Click 'Create & Initialize Database'.", SetupStatusSeverity.Warning);
                CanProceed = false;
            }
            else
            {
                UpdateStatus(result.ErrorMessage ?? "Database exists but could not be opened.", SetupStatusSeverity.Error);
                CanProceed = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Connection test failed unexpectedly.", SetupStatusSeverity.Error);
            TechnicalDetails = ex.Message;
            CanProceed = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    public async Task InitializeDatabaseAsync(CancellationToken ct = default)
    {
        IsInitializing = true;
        TechnicalDetails = null;
        UpdateStatus("Creating database and applying schema migrations...", SetupStatusSeverity.Info);

        try
        {
            var config = BuildCurrentConfig();
            string connStr = _connectionStringFactory.BuildConnectionString(config);

            var regSettings = new RegionalSettingsDto
            {
                CountryCode = SelectedCurrency?.CountryCode ?? "PK",
                CurrencyCode = SelectedCurrency?.CurrencyCode ?? "PKR",
                CurrencySymbol = SelectedCurrency?.Symbol ?? "Rs.",
                CurrencyDecimalPlaces = SelectedCurrency?.DefaultDecimalPlaces ?? 2,
                CultureName = SelectedCurrency?.CultureName ?? "en-PK",
                DefaultLanguageCode = SelectedLanguage?.Code ?? "en"
            };

            var result = await _databaseMigrator.InitializeAndSeedAsync(
                connStr,
                initialRegionalSettings: regSettings,
                initialCompanyName: CompanyName,
                cancellationToken: ct);

            if (result.Success)
            {
                // Persist settings now that initialization succeeded
                _configStore.SaveConfig(config);

                UpdateStatus("Database created, migrated, and baseline configuration seeded successfully! Click 'Proceed to PharmaERP'.", SetupStatusSeverity.Success);
                CanProceed = true;
                IsFirstTimeSetup = false;
            }
            else
            {
                UpdateStatus(result.Message, SetupStatusSeverity.Error);
                TechnicalDetails = result.TechnicalDetails;
                CanProceed = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Failed to initialize database.", SetupStatusSeverity.Error);
            TechnicalDetails = ex.Message;
            CanProceed = false;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    public void SaveAndProceed()
    {
        if (!CanProceed) return;

        var config = BuildCurrentConfig();
        _configStore.SaveConfig(config);
        RequestClose?.Invoke(true);
    }

    private DatabaseConnectionConfig BuildCurrentConfig()
    {
        return new DatabaseConnectionConfig
        {
            Server = Server.Trim(),
            Database = Database.Trim(),
            AuthType = IsWindowsAuth ? DatabaseAuthType.Windows : DatabaseAuthType.Sql,
            UserName = IsWindowsAuth ? null : UserName.Trim(),
            EncryptedPassword = IsWindowsAuth ? null : _credentialProtector.Protect(Password),
            ActiveProfile = "Custom",
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectionTimeoutSeconds = 15,
            Description = $"{Server} ({Database})"
        };
    }

    private void UpdateStatus(string message, SetupStatusSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }
}
