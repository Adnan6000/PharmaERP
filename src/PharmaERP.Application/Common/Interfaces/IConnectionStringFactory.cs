using PharmaERP.Application.Common.Models;

namespace PharmaERP.Application.Common.Interfaces;

/// <summary>
/// Service contract for constructing database connection strings from neutral configuration models.
/// Implementation resides in Infrastructure/Desktop using SqlConnectionStringBuilder.
/// </summary>
public interface IConnectionStringFactory
{
    string BuildConnectionString(DatabaseConnectionConfig config, string? databaseOverride = null, int? timeoutOverrideSeconds = null);
    string BuildMaskedConnectionString(DatabaseConnectionConfig config, string? databaseOverride = null);
}

