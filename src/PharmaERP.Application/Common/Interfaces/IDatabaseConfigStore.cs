using PharmaERP.Application.Common.Models;

namespace PharmaERP.Application.Common.Interfaces;

/// <summary>
/// Authoritative workstation database configuration storage contract.
/// </summary>
public interface IDatabaseConfigStore
{
    bool ConfigExists();
    DatabaseConnectionConfig? LoadConfig();
    void SaveConfig(DatabaseConnectionConfig config);
}

