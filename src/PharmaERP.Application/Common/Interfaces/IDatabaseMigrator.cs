using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public record DatabaseMigrationResult(bool Success, string Message, string? TechnicalDetails = null);

public interface IDatabaseMigrator
{
    Task<DatabaseMigrationResult> MigrateDatabaseAsync(CancellationToken cancellationToken = default);

    Task<DatabaseMigrationResult> MigrateDatabaseAsync(string connectionString, CancellationToken cancellationToken = default);

    Task<DatabaseMigrationResult> InitializeAndSeedAsync(
        string connectionString,
        RegionalSettingsDto? initialRegionalSettings = null,
        string? initialCompanyName = null,
        CancellationToken cancellationToken = default);
}

