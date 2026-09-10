using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;

namespace PharmaERP.Infrastructure.Persistence;

public class DbConnectionTester : IDbConnectionTester
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private readonly IConnectionStringFactory _connectionStringFactory;
    private readonly IDatabaseConfigStore _databaseConfigStore;
    private readonly ILogger<DbConnectionTester> _logger;

    public DbConnectionTester(
        IDbContextFactory<AppDbContext> contextFactory,
        IConfiguration configuration,
        IConnectionStringFactory connectionStringFactory,
        IDatabaseConfigStore databaseConfigStore,
        ILogger<DbConnectionTester> logger)
    {
        _contextFactory = contextFactory;
        _configuration = configuration;
        _connectionStringFactory = connectionStringFactory;
        _databaseConfigStore = databaseConfigStore;
        _logger = logger;
    }

    public async Task<DbConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = _databaseConfigStore.LoadConfig();
        if (config != null && config.IsConfigured)
        {
            return await TestConfigAsync(config, cancellationToken);
        }

        // Fallback to active profile in appsettings
        var activeProfile = DependencyInjection.ResolveActiveProfile(_configuration);
        var description = _configuration[$"DatabaseConfig:Profiles:{activeProfile}:Description"]
            ?? $"{activeProfile} Database Profile";

        var fallbackConfig = new DatabaseConnectionConfig
        {
            ActiveProfile = activeProfile,
            Description = description,
            Server = activeProfile.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "(localdb)\\mssqllocaldb" : ".\\SQLEXPRESS",
            Database = activeProfile.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "PharmaERP_Dev" : "PharmaERP",
            AuthType = DatabaseAuthType.Windows,
            TrustServerCertificate = true,
            ConnectionTimeoutSeconds = 5
        };

        return await TestConfigAsync(fallbackConfig, cancellationToken);
    }

    public async Task<DbConnectionTestResult> TestProfileConnectionAsync(
        string profileName,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            await using var context = new AppDbContext(optionsBuilder.Options);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);

            bool isInitialized = false;
            int appliedCount = 0;
            int pendingCount = 0;

            if (canConnect)
            {
                try
                {
                    var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                    var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                    appliedCount = applied.Count;
                    pendingCount = pending.Count;
                    isInitialized = appliedCount > 0 && pendingCount == 0;
                }
                catch
                {
                    isInitialized = false;
                }
            }

            stopwatch.Stop();

            return new DbConnectionTestResult(
                IsConnected: canConnect,
                IsDatabaseInitialized: isInitialized,
                ActiveProfile: profileName,
                ServerDescription: $"Custom test connection for {profileName}",
                ErrorMessage: canConnect ? null : "Database server responded, but database could not be opened.",
                Latency: stopwatch.Elapsed,
                ServerReachable: canConnect,
                DatabaseExists: canConnect,
                CanCreateDatabase: true,
                AppliedMigrationsCount: appliedCount,
                PendingMigrationsCount: pendingCount);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Profile connection test failed for '{Profile}'.", profileName);

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: profileName,
                ServerDescription: $"Custom test connection for {profileName}",
                ErrorMessage: FormatSqlError(ex),
                Latency: stopwatch.Elapsed);
        }
    }

    public async Task<DbConnectionTestResult> TestConfigAsync(
        DatabaseConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var stopwatch = Stopwatch.StartNew();

        string serverDesc = !string.IsNullOrWhiteSpace(config.Description)
            ? config.Description
            : $"{config.Server} ({config.Database})";

        // Step 1: Test Server Reachability & Authentication using 'master'
        string masterConnStr = _connectionStringFactory.BuildConnectionString(
            config,
            databaseOverride: "master",
            timeoutOverrideSeconds: Math.Min(config.ConnectionTimeoutSeconds > 0 ? config.ConnectionTimeoutSeconds : 5, 5));

        bool databaseExists = false;
        bool canCreateDatabase = false;

        try
        {
            await using var masterConn = new SqlConnection(masterConnStr);
            await masterConn.OpenAsync(cancellationToken);

            // Step 2: Check database existence & CREATE DATABASE permission via master
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    DB_ID(@DbName) AS DatabaseId,
                    HAS_PERMS_BY_NAME(null, null, 'CREATE ANY DATABASE') AS CanCreateDb;";
            cmd.Parameters.Add(new SqlParameter("@DbName", config.Database));

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                databaseExists = !reader.IsDBNull(0);
                canCreateDatabase = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
            }
        }
        catch (SqlException sqlEx)
        {
            stopwatch.Stop();
            string friendlyMsg = FormatSqlError(sqlEx);
            _logger.LogWarning("SQL server ping failed for '{Server}': {Message}", config.Server, friendlyMsg);

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: friendlyMsg,
                Latency: stopwatch.Elapsed,
                ServerReachable: false,
                DatabaseExists: false,
                CanCreateDatabase: false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Unexpected error testing connection for '{Server}'.", config.Server);

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: ex.Message,
                Latency: stopwatch.Elapsed,
                ServerReachable: false,
                DatabaseExists: false,
                CanCreateDatabase: false);
        }

        // Step 3: If target database does not exist yet
        if (!databaseExists)
        {
            stopwatch.Stop();
            string msg = canCreateDatabase
                ? $"Server is reachable. Target database '{config.Database}' does not exist yet. Click 'Create & Initialize Database' to create it."
                : $"Server is reachable, but user does not have CREATE DATABASE permission on '{config.Server}'. An administrator must create '{config.Database}' first.";

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: canCreateDatabase ? null : msg,
                Latency: stopwatch.Elapsed,
                ServerReachable: true,
                DatabaseExists: false,
                CanCreateDatabase: canCreateDatabase);
        }

        // Step 4: Target database exists — test direct connection and migration state
        string targetConnStr = _connectionStringFactory.BuildConnectionString(
            config,
            timeoutOverrideSeconds: Math.Min(config.ConnectionTimeoutSeconds > 0 ? config.ConnectionTimeoutSeconds : 6, 6));

        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(targetConnStr);

            await using var context = new AppDbContext(optionsBuilder.Options);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);

            int appliedCount = 0;
            int pendingCount = 0;
            bool isInitialized = false;

            if (canConnect)
            {
                var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                appliedCount = applied.Count;
                pendingCount = pending.Count;
                isInitialized = appliedCount > 0 && pendingCount == 0;
            }

            stopwatch.Stop();

            string? statusMsg = null;
            if (!canConnect)
            {
                statusMsg = $"Database '{config.Database}' exists, but could not be opened by login.";
            }
            else if (!isInitialized)
            {
                statusMsg = pendingCount > 0
                    ? $"Database connected. {pendingCount} schema migrations are pending."
                    : "Database connected, but schema is uninitialized.";
            }

            return new DbConnectionTestResult(
                IsConnected: canConnect,
                IsDatabaseInitialized: isInitialized,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: statusMsg,
                Latency: stopwatch.Elapsed,
                ServerReachable: true,
                DatabaseExists: true,
                CanCreateDatabase: canCreateDatabase,
                AppliedMigrationsCount: appliedCount,
                PendingMigrationsCount: pendingCount);
        }
        catch (SqlException sqlEx)
        {
            stopwatch.Stop();
            string msg = FormatSqlError(sqlEx);
            _logger.LogWarning("Failed connecting to target database '{Db}': {Msg}", config.Database, msg);

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: msg,
                Latency: stopwatch.Elapsed,
                ServerReachable: true,
                DatabaseExists: true,
                CanCreateDatabase: canCreateDatabase);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: config.ActiveProfile,
                ServerDescription: serverDesc,
                ErrorMessage: ex.Message,
                Latency: stopwatch.Elapsed,
                ServerReachable: true,
                DatabaseExists: true,
                CanCreateDatabase: canCreateDatabase);
        }
    }

    private static string FormatSqlError(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            if (sqlEx.Number is 2 or 53 or 64 or 258 or 121 or 10060 or 10061 ||
                sqlEx.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                sqlEx.Message.Contains("server was not found", StringComparison.OrdinalIgnoreCase))
            {
                return "SQL Server is unreachable. Please verify server name, ensure SQL Server is running, and check TCP/IP / firewall settings.";
            }

            return sqlEx.Number switch
            {
                18456 or 233 => "Login failed. Please verify credentials or ensure Mixed Mode authentication is enabled on SQL Server.",
                4060 => "Database does not exist or user lacks permission to access it.",
                _ => $"SQL Server Error ({sqlEx.Number}): {sqlEx.Message}"
            };
        }

        return ex.Message;
    }
}
