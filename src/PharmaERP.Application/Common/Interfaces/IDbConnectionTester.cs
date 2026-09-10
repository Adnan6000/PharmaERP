namespace PharmaERP.Application.Common.Interfaces;

public record DbConnectionTestResult(
    bool IsConnected,
    bool IsDatabaseInitialized,
    string ActiveProfile,
    string ServerDescription,
    string? ErrorMessage,
    TimeSpan Latency,
    bool ServerReachable = false,
    bool DatabaseExists = false,
    bool CanCreateDatabase = false,
    int AppliedMigrationsCount = 0,
    int PendingMigrationsCount = 0);

/// <summary>
/// Service contract for asynchronous, non-blocking database connectivity verification.
/// </summary>
public interface IDbConnectionTester
{
    Task<DbConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<DbConnectionTestResult> TestProfileConnectionAsync(string profileName, string connectionString, CancellationToken cancellationToken = default);

    Task<DbConnectionTestResult> TestConfigAsync(PharmaERP.Application.Common.Models.DatabaseConnectionConfig config, CancellationToken cancellationToken = default);
}

