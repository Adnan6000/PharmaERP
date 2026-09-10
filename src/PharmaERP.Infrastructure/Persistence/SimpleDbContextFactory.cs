using Microsoft.EntityFrameworkCore;

namespace PharmaERP.Infrastructure.Persistence;

/// <summary>
/// Lightweight IDbContextFactory implementation for standalone connection strings (e.g. bootstrap preflight or tests).
/// </summary>
public class SimpleDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly string _connectionString;

    public SimpleDbContextFactory(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        _connectionString = connectionString;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            })
            .Options;

        return new AppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}

