using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;
using PharmaERP.Infrastructure.Persistence;

namespace PharmaERP.Infrastructure.Tests;

[Collection("SqlServerDatabaseCollection")]
public class RegionalSettingsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerTestFixture _fixture;

    public RegionalSettingsIntegrationTests(SqlServerTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetRegionalSettings_WhenDatabaseFresh_BootstrapsAndReturnsPakistaniDefaults()
    {
        // Arrange
        var regionalService = _fixture.CreateRegionalSettingsService();

        // Act
        var settings = await regionalService.GetRegionalSettingsAsync();

        // Assert
        Assert.NotNull(settings);
        Assert.Equal("PK", settings.CountryCode);
        Assert.Equal("PKR", settings.CurrencyCode);
        Assert.Equal("Rs.", settings.CurrencySymbol);
        Assert.Equal(2, settings.CurrencyDecimalPlaces);
        Assert.Equal("en-PK", settings.CultureName);
        Assert.Equal("en", settings.DefaultLanguageCode);
        Assert.False(settings.IsBaseCurrencyLocked);
    }

    [Fact]
    public async Task SaveRegionalSettings_WhenNoPostedJournals_UpdatesDatabaseSuccessfully()
    {
        // Arrange
        var regionalService = _fixture.CreateRegionalSettingsService();

        var newSettings = new RegionalSettingsDto
        {
            CountryCode = "US",
            CurrencyCode = "USD",
            CurrencySymbol = "$",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-US",
            DefaultLanguageCode = "en"
        };

        // Act
        await regionalService.SaveRegionalSettingsAsync(newSettings);
        var reloaded = await regionalService.GetRegionalSettingsAsync();

        // Assert
        Assert.Equal("USD", reloaded.CurrencyCode);
        Assert.Equal("$", reloaded.CurrencySymbol);
        Assert.Equal("US", reloaded.CountryCode);
        Assert.Equal("en-US", reloaded.CultureName);
        Assert.False(reloaded.IsBaseCurrencyLocked);
    }

    [Fact]
    public async Task SaveRegionalSettings_WhenPostedJournalsExist_LocksBaseCurrency()
    {
        // Arrange
        var regionalService = _fixture.CreateRegionalSettingsService();
        await regionalService.GetRegionalSettingsAsync(); // Ensure defaults

        // Initialize Chart of Accounts and post a valid journal adhering to DB double-entry triggers
        var setupService = _fixture.CreateAccountingSetupService();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var accountService = _fixture.CreateAccountService();
        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var equityAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity);

        await using (var context = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var journal = new JournalEntry
            {
                EntryNumber = "JE-TEST-LOCK-01",
                BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
                PostedAtUtc = DateTime.UtcNow,
                Status = JournalEntryStatus.Draft,
                SourceDocumentType = JournalSourceDocumentType.Manual,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid(),
                Narration = "Test posted journal for base currency lock"
            };
            journal.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 500m, CreditAmount = 0m });
            journal.Lines.Add(new JournalEntryLine { AccountId = equityAcc!.Id, DebitAmount = 0m, CreditAmount = 500m });
            await PharmaERP.Infrastructure.Persistence.Services.AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, journal, default);
        }

        // Verify status reflects lock
        var settingsBefore = await regionalService.GetRegionalSettingsAsync();
        Assert.True(settingsBefore.IsBaseCurrencyLocked);

        var attemptChange = new RegionalSettingsDto
        {
            CountryCode = "GB",
            CurrencyCode = "GBP",
            CurrencySymbol = "£",
            CurrencyDecimalPlaces = 2,
            CultureName = "en-GB",
            DefaultLanguageCode = "en"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => regionalService.SaveRegionalSettingsAsync(attemptChange));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify database value remained PKR
        var settingsAfter = await regionalService.GetRegionalSettingsAsync();
        Assert.Equal("PKR", settingsAfter.CurrencyCode);
    }
}
