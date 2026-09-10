using Microsoft.Data.SqlClient;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;

namespace PharmaERP.Infrastructure.Persistence;

/// <summary>
/// Authoritative builder for SQL Server connection strings using SqlConnectionStringBuilder.
/// Prevents manual connection string concatenation and credential leakage.
/// </summary>
public class SqlConnectionStringFactory : IConnectionStringFactory
{
    private readonly ICredentialProtector _credentialProtector;

    public SqlConnectionStringFactory(ICredentialProtector credentialProtector)
    {
        _credentialProtector = credentialProtector;
    }

    public string BuildConnectionString(
        DatabaseConnectionConfig config,
        string? databaseOverride = null,
        int? timeoutOverrideSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(config.Server) ? ".\\SQLEXPRESS" : config.Server,
            InitialCatalog = databaseOverride ?? (string.IsNullOrWhiteSpace(config.Database) ? "PharmaERP" : config.Database),
            MultipleActiveResultSets = true,
            Encrypt = config.Encrypt,
            TrustServerCertificate = config.TrustServerCertificate,
            ConnectTimeout = timeoutOverrideSeconds ?? (config.ConnectionTimeoutSeconds > 0 ? config.ConnectionTimeoutSeconds : 15)
        };

        if (config.AuthType == DatabaseAuthType.Sql)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = config.UserName ?? string.Empty;
            builder.Password = _credentialProtector.Unprotect(config.EncryptedPassword) ?? string.Empty;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    public string BuildMaskedConnectionString(DatabaseConnectionConfig config, string? databaseOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(config.Server) ? ".\\SQLEXPRESS" : config.Server,
            InitialCatalog = databaseOverride ?? (string.IsNullOrWhiteSpace(config.Database) ? "PharmaERP" : config.Database),
            MultipleActiveResultSets = true,
            Encrypt = config.Encrypt,
            TrustServerCertificate = config.TrustServerCertificate,
            ConnectTimeout = config.ConnectionTimeoutSeconds > 0 ? config.ConnectionTimeoutSeconds : 15
        };

        if (config.AuthType == DatabaseAuthType.Sql)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = config.UserName ?? string.Empty;
            builder.Password = string.IsNullOrEmpty(config.EncryptedPassword) ? string.Empty : "******";
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}

