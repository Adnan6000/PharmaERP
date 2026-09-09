using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Interfaces;

namespace PharmaERP.Infrastructure.Persistence.Services;

/// <summary>
/// Server-authoritative business clock that synchronizes with local SQL Server UTC time
/// and translates to the configured business timezone.
/// </summary>
public class BusinessClock : IBusinessClock
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAppConfigRepository _appConfigRepository;

    public BusinessClock(
        IDbContextFactory<AppDbContext> contextFactory,
        IAppConfigRepository appConfigRepository)
    {
        _contextFactory = contextFactory;
        _appConfigRepository = appConfigRepository;
    }

    public async Task<DateTime> GetServerUtcNowAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SYSUTCDATETIME()";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        var dt = (DateTime)result!;
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    public async Task<DateOnly> GetBusinessDateAsync(CancellationToken cancellationToken = default)
    {
        var utc = await GetServerUtcNowAsync(cancellationToken);
        return await ToBusinessDateAsync(utc, cancellationToken);
    }

    public async Task<DateTime> ToBusinessTimeAsync(DateTime utcDateTime, CancellationToken cancellationToken = default)
    {
        var tz = await GetBusinessTimeZoneAsync(cancellationToken);
        var utc = utcDateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)
            : utcDateTime.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public async Task<DateOnly> ToBusinessDateAsync(DateTime utcDateTime, CancellationToken cancellationToken = default)
    {
        var local = await ToBusinessTimeAsync(utcDateTime, cancellationToken);
        return DateOnly.FromDateTime(local);
    }

    private async Task<TimeZoneInfo> GetBusinessTimeZoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _appConfigRepository.GetBusinessProfileAsync(cancellationToken);
            return ResolveTimeZone(profile.TimeZoneId);
        }
        catch
        {
            return ResolveTimeZone("Pakistan Standard Time");
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        var id = string.IsNullOrWhiteSpace(tzId) ? "Pakistan Standard Time" : tzId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            if (id.Equals("Pakistan Standard Time", StringComparison.OrdinalIgnoreCase) ||
                id.Equals("Asia/Karachi", StringComparison.OrdinalIgnoreCase))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"); } catch { }
                try { return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"); } catch { }
            }
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

