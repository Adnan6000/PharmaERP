using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Interfaces;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class AccountingOperationalGate : IAccountingOperationalGate
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    public const string ResourceName = "PharmaERP_AccountingOperationalGate";

    public AccountingOperationalGate(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IAsyncDisposable> AcquireSharedOperationalGateAsync(CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DECLARE @res INT;
            EXEC @res = sp_getapplock
                @Resource = @ResourceName,
                @LockMode = 'Shared',
                @LockOwner = 'Session',
                @LockTimeout = 30000;
            SELECT @res;";
        cmd.Parameters.AddWithValue("@ResourceName", ResourceName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        int lockCode = Convert.ToInt32(result);
        if (lockCode < 0)
        {
            await context.DisposeAsync();
            throw new InvalidOperationException($"Failed to acquire shared operational gate lock. Code: {lockCode}");
        }

        return new GateReleaser(context, connection, ResourceName);
    }

    public async Task<IAsyncDisposable> AcquireExclusiveInitializationGateAsync(CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DECLARE @res INT;
            EXEC @res = sp_getapplock
                @Resource = @ResourceName,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 60000;
            SELECT @res;";
        cmd.Parameters.AddWithValue("@ResourceName", ResourceName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        int lockCode = Convert.ToInt32(result);
        if (lockCode < 0)
        {
            await context.DisposeAsync();
            throw new InvalidOperationException($"Failed to acquire exclusive initialization gate lock. Code: {lockCode}");
        }

        return new GateReleaser(context, connection, ResourceName);
    }

    private sealed class GateReleaser : IAsyncDisposable
    {
        private readonly AppDbContext _context;
        private readonly SqlConnection _connection;
        private readonly string _resource;
        private bool _disposed;

        public GateReleaser(AppDbContext context, SqlConnection connection, string resource)
        {
            _context = context;
            _connection = connection;
            _resource = resource;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "EXEC sp_releaseapplock @Resource = @ResourceName, @LockOwner = 'Session';";
                    cmd.Parameters.AddWithValue("@ResourceName", _resource);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Suppress release errors on dispose
            }
            finally
            {
                await _context.DisposeAsync();
            }
        }
    }
}
