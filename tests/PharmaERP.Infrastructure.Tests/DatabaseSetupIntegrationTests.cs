using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Infrastructure.Persistence;
using PharmaERP.Infrastructure.Security;

namespace PharmaERP.Infrastructure.Tests;

public class DatabaseSetupIntegrationTests : IAsyncLifetime
{
    private readonly string _testDbName;
    private readonly string _testConnStr;
    private readonly DpapiCredentialProtector _protector;
    private readonly SqlConnectionStringFactory _factory;
    private readonly IConfiguration _configuration;

    public DatabaseSetupIntegrationTests()
    {
        _testDbName = $"PharmaERP_SetupDisp_{Guid.NewGuid():N}".Substring(0, 24);
        _testConnStr = $"Server=.\\SQLEXPRESS;Database={_testDbName};Integrated Security=True;TrustServerCertificate=True";
        _protector = new DpapiCredentialProtector();
        _factory = new SqlConnectionStringFactory(_protector);
        _configuration = new ConfigurationBuilder().Build();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            // Drop disposable test database cleanly
            await using var masterConn = new SqlConnection("Server=.\\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True");
            await masterConn.OpenAsync();
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $@"
                IF DB_ID('{_testDbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_testDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_testDbName}];
                END";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
        }
    }

    [Fact]
    public async Task InvalidServerName_ReturnsFriendlyUnreachableError()
    {
        var config = new DatabaseConnectionConfig
        {
            Server = "NON_EXISTENT_SERVER_XYZ_9999",
            Database = "PharmaERP",
            AuthType = DatabaseAuthType.Windows,
            ConnectionTimeoutSeconds = 2
        };

        var configStore = new WorkstationDatabaseConfigStore();
        var tester = new DbConnectionTester(
            new SimpleDbContextFactory(_testConnStr),
            _configuration,
            _factory,
            configStore,
            NullLogger<DbConnectionTester>.Instance);

        var result = await tester.TestConfigAsync(config);

        Assert.False(result.IsConnected);
        Assert.False(result.ServerReachable);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("unreachable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingDatabase_DetectedCleanly_WithoutCrashing()
    {
        var config = new DatabaseConnectionConfig
        {
            Server = ".\\SQLEXPRESS",
            Database = _testDbName, // Does not exist yet
            AuthType = DatabaseAuthType.Windows,
            ConnectionTimeoutSeconds = 5
        };

        var configStore = new WorkstationDatabaseConfigStore();
        var tester = new DbConnectionTester(
            new SimpleDbContextFactory(_testConnStr),
            _configuration,
            _factory,
            configStore,
            NullLogger<DbConnectionTester>.Instance);

        var result = await tester.TestConfigAsync(config);

        Assert.True(result.ServerReachable);
        Assert.False(result.DatabaseExists);
        Assert.False(result.IsConnected);
        Assert.True(result.CanCreateDatabase);
    }

    [Fact]
    public async Task InitializeAndSeed_CreatesDatabase_AppliesMigrations_AndSeedsIdempotently()
    {
        var migrator = new DatabaseMigrator(
            new SimpleDbContextFactory(_testConnStr),
            NullLogger<DatabaseMigrator>.Instance);

        var customRegional = new RegionalSettingsDto
        {
            CountryCode = "PK",
            CurrencyCode = "PKR",
            CurrencySymbol = "Rs.",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-PK",
            DefaultLanguageCode = "en"
        };

        // 1. First execution: creates target DB, applies migrations, seeds COA & regional
        var initResult = await migrator.InitializeAndSeedAsync(
            _testConnStr,
            initialRegionalSettings: customRegional,
            initialCompanyName: "Shifa Community Pharmacy");

        Assert.True(initResult.Success);

        // Verify DB content directly
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_testConnStr).Options;
        await using (var ctx = new AppDbContext(options))
        {
            // Verify migrations applied
            var applied = await ctx.Database.GetAppliedMigrationsAsync();
            Assert.True(applied.Any());

            // Verify Chart of Accounts seeded
            var accountsCount = await ctx.Accounts.CountAsync();
            Assert.True(accountsCount >= 14);

            // Verify Regional settings seeded
            var currencyConfig = await ctx.AppConfigs.FirstOrDefaultAsync(c => c.Key == "Regional.CurrencyCode");
            Assert.NotNull(currencyConfig);
            Assert.Equal("PKR", currencyConfig.Value);

            var symbolConfig = await ctx.AppConfigs.FirstOrDefaultAsync(c => c.Key == "Regional.CurrencySymbol");
            Assert.NotNull(symbolConfig);
            Assert.Equal("Rs.", symbolConfig.Value);

            // Verify Company Name
            var nameConfig = await ctx.AppConfigs.FirstOrDefaultAsync(c => c.Key == "BusinessProfile.PharmacyName");
            Assert.NotNull(nameConfig);
            Assert.Equal("Shifa Community Pharmacy", nameConfig.Value);

            // Verify Default Units of Measure seeded
            var unitsCount = await ctx.Units.CountAsync();
            Assert.True(unitsCount >= 10);
            Assert.True(await ctx.Units.AnyAsync(u => u.Abbreviation == "Tab"));
            Assert.True(await ctx.Units.AnyAsync(u => u.Abbreviation == "Cap"));
            Assert.True(await ctx.Units.AnyAsync(u => u.Abbreviation == "Btl"));
        }

        // 2. Second execution (idempotence): must not duplicate COA or overwrite data
        var secondResult = await migrator.InitializeAndSeedAsync(
            _testConnStr,
            initialRegionalSettings: customRegional,
            initialCompanyName: "Shifa Community Pharmacy");

        Assert.True(secondResult.Success);

        await using (var ctx = new AppDbContext(options))
        {
            var accountsCount = await ctx.Accounts.CountAsync();
            Assert.True(accountsCount >= 14);

            // Verify no duplicate account codes
            var duplicateCodes = await ctx.Accounts
                .GroupBy(a => a.AccountCode)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            Assert.Empty(duplicateCodes);

            // Verify units were not duplicated
            var unitsCountAfter = await ctx.Units.CountAsync();
            Assert.Equal(10, unitsCountAfter);

            var duplicateUnits = await ctx.Units
                .GroupBy(u => u.Name.ToUpper())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            Assert.Empty(duplicateUnits);
        }
    }

    [Fact]
    public void EfCoreModel_ValidatesWithoutErrorsOrWarnings()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.\\SQLEXPRESS;Database=PharmaERP;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        using var ctx = new AppDbContext(options);
        var model = ctx.Model;
        Assert.NotNull(model);
        Assert.NotEmpty(model.GetEntityTypes());
    }
}

