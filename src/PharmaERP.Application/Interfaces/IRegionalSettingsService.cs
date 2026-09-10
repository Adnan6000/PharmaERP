using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Interfaces;

public interface IRegionalSettingsService
{
    /// <summary>
    /// Gets the company's authoritative regional and base currency settings.
    /// </summary>
    Task<RegionalSettingsDto> GetRegionalSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the company's authoritative regional and base currency settings.
    /// Throws InvalidOperationException if an attempt is made to change CurrencyCode after financial transactions exist.
    /// </summary>
    Task SaveRegionalSettingsAsync(RegionalSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if the company database contains any authoritative posted financial transactions (posted GL journals).
    /// </summary>
    Task<bool> HasPostedFinancialTransactionsAsync(CancellationToken cancellationToken = default);
}

