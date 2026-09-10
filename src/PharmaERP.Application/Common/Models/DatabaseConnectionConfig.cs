namespace PharmaERP.Application.Common.Models;

public enum DatabaseAuthType
{
    Windows = 0,
    Sql = 1
}

/// <summary>
/// Neutral database connection configuration model.
/// Contains no SQL Server-specific string builders or platform-specific DPAPI logic.
/// </summary>
public class DatabaseConnectionConfig
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = "PharmaERP";
    public DatabaseAuthType AuthType { get; set; } = DatabaseAuthType.Windows;
    public string? UserName { get; set; }
    public string? EncryptedPassword { get; set; }
    public string ActiveProfile { get; set; } = "Custom";
    public bool Encrypt { get; set; } = false;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectionTimeoutSeconds { get; set; } = 15;
    public string? Description { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Database);
}

