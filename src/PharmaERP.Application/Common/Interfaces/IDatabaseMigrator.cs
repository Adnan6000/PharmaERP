namespace PharmaERP.Application.Common.Interfaces;

public record DatabaseMigrationResult(bool Success, string Message, string? TechnicalDetails = null);

public interface IDatabaseMigrator
{
    Task<DatabaseMigrationResult> MigrateDatabaseAsync(CancellationToken cancellationToken = default);
}

