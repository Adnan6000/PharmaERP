using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;

namespace PharmaERP.Application.Services;

public class RegionalSettingsService : IRegionalSettingsService
{
    private readonly IAppConfigRepository _appConfigRepository;

    public RegionalSettingsService(IAppConfigRepository appConfigRepository)
    {
        _appConfigRepository = appConfigRepository;
    }

    public async Task<RegionalSettingsDto> GetRegionalSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _appConfigRepository.GetRegionalSettingsAsync(cancellationToken);
        settings.IsBaseCurrencyLocked = await HasPostedFinancialTransactionsAsync(cancellationToken);
        return settings;
    }

    public async Task SaveRegionalSettingsAsync(RegionalSettingsDto settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool isLocked = await HasPostedFinancialTransactionsAsync(cancellationToken);
        if (isLocked)
        {
            var existing = await _appConfigRepository.GetRegionalSettingsAsync(cancellationToken);
            if (!string.Equals(existing.CurrencyCode, settings.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Base currency '{existing.CurrencyCode}' is locked because posted financial transactions exist. " +
                    $"Cannot switch base currency to '{settings.CurrencyCode}'.");
            }
        }

        await _appConfigRepository.SaveRegionalSettingsAsync(settings, cancellationToken);
    }

    public async Task<bool> HasPostedFinancialTransactionsAsync(CancellationToken cancellationToken = default)
    {
        return await _appConfigRepository.HasPostedFinancialTransactionsAsync(cancellationToken);
    }
}

