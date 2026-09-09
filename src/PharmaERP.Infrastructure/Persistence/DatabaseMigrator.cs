using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;

namespace PharmaERP.Infrastructure.Persistence;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DatabaseMigrator> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<DatabaseMigrationResult> MigrateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initiating explicit database migration using Database.MigrateAsync.");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);

            _logger.LogInformation("Database migration completed successfully.");
            return new DatabaseMigrationResult(true, "Database initialized and upgraded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed.");
            return new DatabaseMigrationResult(
                false,
                "Failed to initialize or upgrade database. Please verify SQL Server is running and connection settings are correct.",
                ex.Message);
        }
    }
}

