using System.Text.Json;
using PharmaERP.Application.Common.Models;
using PharmaERP.Infrastructure.Persistence;
using PharmaERP.Infrastructure.Security;

namespace PharmaERP.Infrastructure.Tests;

public class DatabaseConfigTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public DatabaseConfigTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"pharmaerp_test_settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsPath))
            {
                File.Delete(_tempSettingsPath);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void NoConfig_FirstRun_ReturnsNull()
    {
        var store = new WorkstationDatabaseConfigStore(customSettingsPath: _tempSettingsPath);

        Assert.False(store.ConfigExists());
        Assert.Null(store.LoadConfig());
    }

    [Fact]
    public void ConnectionProfile_Serialization_RoundTrip()
    {
        var store = new WorkstationDatabaseConfigStore(customSettingsPath: _tempSettingsPath);
        var protector = new DpapiCredentialProtector();

        var original = new DatabaseConnectionConfig
        {
            Server = "SRV-TEST\\SQL2022",
            Database = "PharmaERP_Client01",
            AuthType = DatabaseAuthType.Sql,
            UserName = "pharma_user",
            EncryptedPassword = protector.Protect("P@ssw0rd123!"),
            ActiveProfile = "Custom",
            Encrypt = true,
            TrustServerCertificate = false,
            ConnectionTimeoutSeconds = 25,
            Description = "Office Server"
        };

        store.SaveConfig(original);

        Assert.True(store.ConfigExists());
        var loaded = store.LoadConfig();

        Assert.NotNull(loaded);
        Assert.Equal(original.Server, loaded.Server);
        Assert.Equal(original.Database, loaded.Database);
        Assert.Equal(original.AuthType, loaded.AuthType);
        Assert.Equal(original.UserName, loaded.UserName);
        Assert.Equal(original.EncryptedPassword, loaded.EncryptedPassword);
        Assert.Equal(original.ActiveProfile, loaded.ActiveProfile);
        Assert.Equal(original.Encrypt, loaded.Encrypt);
        Assert.Equal(original.TrustServerCertificate, loaded.TrustServerCertificate);
        Assert.Equal(original.ConnectionTimeoutSeconds, loaded.ConnectionTimeoutSeconds);
        Assert.Equal(original.Description, loaded.Description);
    }

    [Fact]
    public void LegacyProfile_RootActiveProfile_RoundTrip()
    {
        var legacyJson = JsonSerializer.Serialize(new { ActiveProfile = "Local" });
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var store = new WorkstationDatabaseConfigStore(customSettingsPath: _tempSettingsPath);

        Assert.True(store.ConfigExists());
        var loaded = store.LoadConfig();

        Assert.NotNull(loaded);
        Assert.Equal("Local", loaded.ActiveProfile);
        Assert.Equal(".\\SQLEXPRESS", loaded.Server);
        Assert.Equal("PharmaERP", loaded.Database);
        Assert.Equal(DatabaseAuthType.Windows, loaded.AuthType);
    }

    [Fact]
    public void LegacyProfile_NestedActiveProfile_RoundTrip()
    {
        var legacyJson = JsonSerializer.Serialize(new
        {
            DatabaseConfig = new { ActiveProfile = "Development" }
        });
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var store = new WorkstationDatabaseConfigStore(customSettingsPath: _tempSettingsPath);

        Assert.True(store.ConfigExists());
        var loaded = store.LoadConfig();

        Assert.NotNull(loaded);
        Assert.Equal("Development", loaded.ActiveProfile);
        Assert.Equal("(localdb)\\mssqllocaldb", loaded.Server);
        Assert.Equal("PharmaERP_Dev", loaded.Database);
    }

    [Fact]
    public void CredentialProtector_Dpapi_RoundTrip()
    {
        var protector = new DpapiCredentialProtector();
        string secret = "SuperSecret_SQL_Password_456$";

        string? cipherText = protector.Protect(secret);

        Assert.NotNull(cipherText);
        Assert.NotEqual(secret, cipherText);

        string? decrypted = protector.Unprotect(cipherText);
        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void SqlConnectionStringFactory_WindowsAuth_GeneratesCorrectly()
    {
        var protector = new DpapiCredentialProtector();
        var factory = new SqlConnectionStringFactory(protector);

        var config = new DatabaseConnectionConfig
        {
            Server = "SERVER-PC\\SQLEXPRESS",
            Database = "PharmaERP",
            AuthType = DatabaseAuthType.Windows,
            Encrypt = false,
            TrustServerCertificate = true
        };

        string connStr = factory.BuildConnectionString(config);

        Assert.Contains("Data Source=SERVER-PC\\SQLEXPRESS", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Initial Catalog=PharmaERP", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Integrated Security=True", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", connStr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlConnectionStringFactory_SqlAuth_GeneratesAndMasksCorrectly()
    {
        var protector = new DpapiCredentialProtector();
        var factory = new SqlConnectionStringFactory(protector);

        var config = new DatabaseConnectionConfig
        {
            Server = "192.168.1.50,1433",
            Database = "PharmaERP_Branch",
            AuthType = DatabaseAuthType.Sql,
            UserName = "sa_app",
            EncryptedPassword = protector.Protect("StrongSqlPassword!789"),
            Encrypt = true,
            TrustServerCertificate = false
        };

        string fullConnStr = factory.BuildConnectionString(config);
        string maskedConnStr = factory.BuildMaskedConnectionString(config);

        // Full connection string has cleartext password for ADO.NET
        Assert.Contains("User ID=sa_app", fullConnStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=StrongSqlPassword!789", fullConnStr);
        Assert.Contains("Encrypt=True", fullConnStr, StringComparison.OrdinalIgnoreCase);

        // Masked connection string hides password
        Assert.Contains("User ID=sa_app", maskedConnStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=******", maskedConnStr);
        Assert.DoesNotContain("StrongSqlPassword!789", maskedConnStr);
    }
}

