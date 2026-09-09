namespace PharmaERP.Application.Common.Interfaces;

public record DbConnectionTestResult(
    bool IsConnected,
    bool IsDatabaseInitialized,
    string ActiveProfile,
    string ServerDescription,
    string? ErrorMessage,
    TimeSpan Latency);

/// <summary>
/// Service contract for asynchronous, non-blocking database connectivity verification.
/// </summary>
public interface IDbConnectionTester
{
    Task<DbConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<DbConnectionTestResult> TestProfileConnectionAsync(string profileName, string connectionString, CancellationToken cancellationToken = default);
}

