namespace PharmaERP.Application.Common.Interfaces;

/// <summary>
/// Authoritative business clock service providing database-server synchronized time
/// and business-local date derived from central business profile timezone configuration.
/// Workstations and clients cannot override this authoritative clock.
/// </summary>
public interface IBusinessClock
{
    /// <summary>
    /// Gets the authoritative current UTC timestamp from the database server.
    /// </summary>
    Task<DateTime> GetServerUtcNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the authoritative business-local calendar date based on server UTC and configured business timezone.
    /// </summary>
    Task<DateOnly> GetBusinessDateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a server UTC timestamp to business-local DateTime using configured business timezone.
    /// </summary>
    Task<DateTime> ToBusinessTimeAsync(DateTime utcDateTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a server UTC timestamp to business-local DateOnly using configured business timezone.
    /// </summary>
    Task<DateOnly> ToBusinessDateAsync(DateTime utcDateTime, CancellationToken cancellationToken = default);
}

