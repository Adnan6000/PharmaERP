namespace PharmaERP.Domain.Common;

/// <summary>
/// Framework-neutral base entity providing identity, audit timestamps, and optimistic concurrency.
/// Note: No persistence-specific attributes or EF Core dependencies are placed here.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Concurrency token for multi-user optimistic concurrency.
    /// Configured as a row version in Infrastructure Fluent API.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}

