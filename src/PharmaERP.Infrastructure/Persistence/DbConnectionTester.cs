using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;

namespace PharmaERP.Infrastructure.Persistence;

public class DbConnectionTester : IDbConnectionTester
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbConnectionTester> _logger;

    public DbConnectionTester(
        IDbContextFactory<AppDbContext> contextFactory,
        IConfiguration configuration,
        ILogger<DbConnectionTester> logger)
    {
        _contextFactory = contextFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<DbConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var activeProfile = DependencyInjection.ResolveActiveProfile(_configuration);
        var description = _configuration[$"DatabaseConfig:Profiles:{activeProfile}:Description"]
            ?? $"{activeProfile} Database Profile";

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);

            bool isInitialized = false;
            if (canConnect)
            {
                try
                {
                    var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
                    isInitialized = applied.Any();
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
                ActiveProfile: activeProfile,
                ServerDescription: description,
                ErrorMessage: canConnect ? null : "Database server was reachable, but database could not be opened.",
                Latency: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Database connection test failed for profile '{Profile}'.", activeProfile);

            return new DbConnectionTestResult(
                IsConnected: false,
                IsDatabaseInitialized: false,
                ActiveProfile: activeProfile,
                ServerDescription: description,
                ErrorMessage: ex.Message,
                Latency: stopwatch.Elapsed);
        }
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
            if (canConnect)
            {
                try
                {
                    var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
                    isInitialized = applied.Any();
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
                Latency: stopwatch.Elapsed);
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
                ErrorMessage: ex.Message,
                Latency: stopwatch.Elapsed);
        }
    }
}

