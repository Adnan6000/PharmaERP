using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DatabaseMigrator> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<DatabaseMigrationResult> MigrateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initiating database migration using pooled context factory.");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await ExecuteMigrationInternalAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed.");
            return new DatabaseMigrationResult(
                false,
                "Failed to initialize or upgrade database. Please verify SQL Server is running and connection settings are correct.",
                ex.Message);
        }
    }

    public async Task<DatabaseMigrationResult> MigrateDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initiating database migration with explicit connection string.");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            await using var context = new AppDbContext(optionsBuilder.Options);
            return await ExecuteMigrationInternalAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed with explicit connection string.");
            return new DatabaseMigrationResult(
                false,
                "Failed to initialize or upgrade database.",
                ex.Message);
        }
    }

    public async Task<DatabaseMigrationResult> InitializeAndSeedAsync(
        string connectionString,
        RegionalSettingsDto? initialRegionalSettings = null,
        string? initialCompanyName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Beginning safe, idempotent database initialization and seeding.");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            await using var context = new AppDbContext(optionsBuilder.Options);

            // 1. Run migrations first (this creates the database if it doesn't exist yet)
            await context.Database.MigrateAsync(cancellationToken);

            // 2. Acquire exclusive application lock via sp_getapplock for multi-PC LAN coordination
            var conn = context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DECLARE @result INT;
                EXEC @result = sp_getapplock
                    @Resource = 'PharmaERP_DatabaseMigration',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 30000;
                SELECT @result;";

            var lockRes = await cmd.ExecuteScalarAsync(cancellationToken);
            int lockCode = lockRes != null ? Convert.ToInt32(lockRes) : -1;

            if (lockCode < 0)
            {
                _logger.LogWarning("sp_getapplock returned {Code}. Another workstation is currently initializing or upgrading.", lockCode);
                return new DatabaseMigrationResult(
                    false,
                    "Database upgrade is in progress by another workstation. Please wait a moment and try again.");
            }

            try
            {
                // 3. Idempotently seed Chart of Accounts if none exist
                await SeedDefaultChartOfAccountsIfEmptyAsync(context, cancellationToken);

                // 4. Idempotently seed system configuration & Accounting Setup Gate
                await SeedSystemConfigIfEmptyAsync(context, initialRegionalSettings, initialCompanyName, cancellationToken);

                _logger.LogInformation("Database initialization, migration, and seeding completed successfully.");
                return new DatabaseMigrationResult(true, "Database initialized, migrated, and verified successfully.");
            }
            finally
            {
                // Release exclusive application lock
                try
                {
                    using var releaseCmd = conn.CreateCommand();
                    releaseCmd.CommandText = "EXEC sp_releaseapplock @Resource = 'PharmaERP_DatabaseMigration', @LockOwner = 'Session';";
                    await releaseCmd.ExecuteNonQueryAsync(CancellationToken.None);
                    _logger.LogInformation("Released exclusive migration lock 'PharmaERP_DatabaseMigration'.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release migration lock gracefully.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization and seeding failed.");
            return new DatabaseMigrationResult(
                false,
                "Database initialization failed. Existing database and data remain preserved.",
                ex.Message);
        }
    }

    private async Task<DatabaseMigrationResult> ExecuteMigrationInternalAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        // First migrate database
        await context.Database.MigrateAsync(cancellationToken);

        // Idempotently verify baseline seeds
        await SeedDefaultChartOfAccountsIfEmptyAsync(context, cancellationToken);
        await SeedSystemConfigIfEmptyAsync(context, null, null, cancellationToken);

        return new DatabaseMigrationResult(true, "Database upgraded successfully.");
    }

    private async Task SeedDefaultChartOfAccountsIfEmptyAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        bool hasAccounts = await context.Accounts.AnyAsync(cancellationToken);
        if (hasAccounts)
        {
            _logger.LogInformation("Existing Chart of Accounts detected. Preserving existing accounts.");
            return;
        }

        _logger.LogInformation("Seeding default standard Chart of Accounts.");

        // 1000 - ASSETS
        var assetsHeader = new Account
        {
            AccountCode = "1000",
            Name = "Assets",
            AccountType = AccountType.Asset,
            AllowPosting = false,
            IsActive = true
        };
        context.Accounts.Add(assetsHeader);
        await context.SaveChangesAsync(cancellationToken);

        var cashOnHand = new Account
        {
            AccountCode = "1010",
            Name = "Cash on Hand",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsCashAccount = true,
            SystemAccountType = SystemAccountType.CashOnHand,
            IsActive = true
        };
        var cashAtBank = new Account
        {
            AccountCode = "1020",
            Name = "Cash at Bank",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsBankAccount = true,
            SystemAccountType = SystemAccountType.DefaultBankAccount,
            IsActive = true
        };
        var arControl = new Account
        {
            AccountCode = "1030",
            Name = "Accounts Receivable Control",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.AccountsReceivableControl,
            IsActive = true
        };
        var inventory = new Account
        {
            AccountCode = "1040",
            Name = "Merchandise Inventory",
            AccountType = AccountType.Asset,
            ParentAccountId = assetsHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.Inventory,
            IsActive = true
        };

        // 2000 - LIABILITIES
        var liabHeader = new Account
        {
            AccountCode = "2000",
            Name = "Liabilities",
            AccountType = AccountType.Liability,
            AllowPosting = false,
            IsActive = true
        };
        context.Accounts.Add(liabHeader);
        await context.SaveChangesAsync(cancellationToken);

        var apControl = new Account
        {
            AccountCode = "2010",
            Name = "Accounts Payable Control",
            AccountType = AccountType.Liability,
            ParentAccountId = liabHeader.Id,
            AllowPosting = true,
            IsControlAccount = true,
            SystemAccountType = SystemAccountType.AccountsPayableControl,
            IsActive = true
        };

        // 3000 - EQUITY
        var equityHeader = new Account
        {
            AccountCode = "3000",
            Name = "Equity",
            AccountType = AccountType.Equity,
            AllowPosting = false,
            IsActive = true
        };
        context.Accounts.Add(equityHeader);
        await context.SaveChangesAsync(cancellationToken);

        var capital = new Account
        {
            AccountCode = "3010",
            Name = "Owner's Capital",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            IsActive = true
        };
        var retainedEarnings = new Account
        {
            AccountCode = "3020",
            Name = "Retained Earnings",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            IsActive = true
        };
        var openingEquity = new Account
        {
            AccountCode = "3030",
            Name = "Opening Balance Equity",
            AccountType = AccountType.Equity,
            ParentAccountId = equityHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.OpeningBalanceEquity,
            IsActive = true
        };

        // 4000 - REVENUE
        var revHeader = new Account
        {
            AccountCode = "4000",
            Name = "Revenue",
            AccountType = AccountType.Revenue,
            AllowPosting = false,
            IsActive = true
        };
        context.Accounts.Add(revHeader);
        await context.SaveChangesAsync(cancellationToken);

        var salesRev = new Account
        {
            AccountCode = "4010",
            Name = "Sales Revenue",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesRevenue,
            IsActive = true
        };
        var salesReturns = new Account
        {
            AccountCode = "4020",
            Name = "Sales Returns & Allowances",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesReturns,
            IsActive = true
        };
        var salesDiscount = new Account
        {
            AccountCode = "4030",
            Name = "Sales Discounts Allowed",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.SalesDiscount,
            IsActive = true
        };
        var purchDiscount = new Account
        {
            AccountCode = "4040",
            Name = "Purchase Discounts Received",
            AccountType = AccountType.Revenue,
            ParentAccountId = revHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.PurchaseDiscount,
            IsActive = true
        };

        // 5000 - EXPENSES & COGS
        var expHeader = new Account
        {
            AccountCode = "5000",
            Name = "Expenses & Cost of Sales",
            AccountType = AccountType.Expense,
            AllowPosting = false,
            IsActive = true
        };
        context.Accounts.Add(expHeader);
        await context.SaveChangesAsync(cancellationToken);

        var cogs = new Account
        {
            AccountCode = "5010",
            Name = "Cost of Goods Sold",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            SystemAccountType = SystemAccountType.CostOfGoodsSold,
            IsActive = true
        };
        var salaries = new Account
        {
            AccountCode = "5020",
            Name = "Salaries & Wages",
            AccountType = AccountType.Expense,
            ParentAccountId = expHeader.Id,
            AllowPosting = true,
            IsActive = true
        };

        context.Accounts.AddRange(
            cashOnHand, cashAtBank, arControl, inventory,
            apControl,
            capital, retainedEarnings, openingEquity,
            salesRev, salesReturns, salesDiscount, purchDiscount,
            cogs, salaries);

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Default Chart of Accounts seeded successfully.");
    }

    private async Task SeedSystemConfigIfEmptyAsync(
        AppDbContext context,
        RegionalSettingsDto? initialRegionalSettings,
        string? initialCompanyName,
        CancellationToken cancellationToken)
    {
        // 1. Accounting Setup State
        var setupState = await context.AppConfigs.FirstOrDefaultAsync(c => c.Key == "Accounting.SetupState", cancellationToken);
        if (setupState == null)
        {
            context.AppConfigs.Add(new AppConfig
            {
                Key = "Accounting.SetupState",
                Value = "Active",
                Description = "Accounting setup gate state"
            });
        }

        // 2. Regional Settings
        var reg = initialRegionalSettings ?? new RegionalSettingsDto();
        var existingReg = await context.AppConfigs.FirstOrDefaultAsync(c => c.Key == "Regional.CurrencyCode", cancellationToken);
        if (existingReg == null)
        {
            _logger.LogInformation("Establishing initial regional settings: {Country}/{Currency} ({Symbol})", reg.CountryCode, reg.CurrencyCode, reg.CurrencySymbol);
            context.AppConfigs.AddRange(
                new AppConfig { Key = "Regional.CountryCode", Value = reg.CountryCode, Description = "Company country code" },
                new AppConfig { Key = "Regional.CurrencyCode", Value = reg.CurrencyCode, Description = "Company base currency code" },
                new AppConfig { Key = "Regional.CurrencySymbol", Value = reg.CurrencySymbol, Description = "Company currency display symbol" },
                new AppConfig { Key = "Regional.CurrencyDecimalPlaces", Value = reg.CurrencyDecimalPlaces.ToString(), Description = "Currency decimal places" },
                new AppConfig { Key = "Regional.CultureName", Value = reg.CultureName, Description = "Regional culture identifier" },
                new AppConfig { Key = "Regional.DefaultLanguageCode", Value = reg.DefaultLanguageCode, Description = "Default company language code" }
            );
        }

        // 3. Business Profile Pharmacy Name
        var existingName = await context.AppConfigs.FirstOrDefaultAsync(c => c.Key == "BusinessProfile.PharmacyName", cancellationToken);
        if (existingName == null)
        {
            string name = !string.IsNullOrWhiteSpace(initialCompanyName) ? initialCompanyName : "PharmaERP Community Pharmacy";
            context.AppConfigs.Add(new AppConfig
            {
                Key = "BusinessProfile.PharmacyName",
                Value = name,
                Description = "Pharmacy display name"
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
