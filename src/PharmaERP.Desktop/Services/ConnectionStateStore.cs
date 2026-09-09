using System.Windows;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Desktop.Common;

namespace PharmaERP.Desktop.Services;

/// <summary>
/// Singleton desktop store providing a single, truthful, observable database connectivity state
/// for all UI views and ViewModels across the application.
/// </summary>
public class ConnectionStateStore : ViewModelBase
{
    private readonly IDbConnectionTester _connectionTester;

    private bool _isConnected;
    private bool _isDatabaseInitialized;
    private string _activeProfile = "Local";
    private string _serverDescription = "SQL Server";
    private string _latencyText = "—";
    private string _statusText = "Checking...";
    private string? _errorMessage;
    private bool _isChecking;
    private DateTime? _lastCheckedAt;

    public ConnectionStateStore(IDbConnectionTester connectionTester)
    {
        _connectionTester = connectionTester;
    }

    public event EventHandler? ConnectionStateChanged;

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetField(ref _isConnected, value);
    }

    public bool IsDatabaseInitialized
    {
        get => _isDatabaseInitialized;
        private set => SetField(ref _isDatabaseInitialized, value);
    }

    public string ActiveProfile
    {
        get => _activeProfile;
        private set => SetField(ref _activeProfile, value);
    }

    public string ServerDescription
    {
        get => _serverDescription;
        private set => SetField(ref _serverDescription, value);
    }

    public string LatencyText
    {
        get => _latencyText;
        private set => SetField(ref _latencyText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetField(ref _isChecking, value);
    }

    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        private set => SetField(ref _lastCheckedAt, value);
    }

    public async Task CheckConnectionAsync(CancellationToken ct = default)
    {
        if (IsChecking)
        {
            return;
        }

        UpdateOnUi(() =>
        {
            IsChecking = true;
            StatusText = "Connecting...";
        });

        try
        {
            var result = await _connectionTester.TestConnectionAsync(ct);

            UpdateOnUi(() =>
            {
                IsConnected = result.IsConnected;
                IsDatabaseInitialized = result.IsDatabaseInitialized;
                ActiveProfile = result.ActiveProfile;
                ServerDescription = result.ServerDescription;
                LastCheckedAt = DateTime.Now;

                if (!result.IsConnected)
                {
                    StatusText = "Disconnected";
                    LatencyText = "—";
                    ErrorMessage = result.ErrorMessage ?? "Database unreachable. Please check network/server settings.";
                }
                else if (!result.IsDatabaseInitialized)
                {
                    StatusText = "Uninitialized";
                    LatencyText = $"{result.Latency.TotalMilliseconds:F0} ms";
                    ErrorMessage = "Database connected but schema migrations are pending.";
                }
                else
                {
                    StatusText = "Connected";
                    LatencyText = $"{result.Latency.TotalMilliseconds:F0} ms";
                    ErrorMessage = null;
                }
            });
        }
        catch (Exception ex)
        {
            UpdateOnUi(() =>
            {
                IsConnected = false;
                IsDatabaseInitialized = false;
                StatusText = "Error";
                LatencyText = "—";
                ErrorMessage = ex.Message;
                LastCheckedAt = DateTime.Now;
            });
        }
        finally
        {
            UpdateOnUi(() =>
            {
                IsChecking = false;
                ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private static void UpdateOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
