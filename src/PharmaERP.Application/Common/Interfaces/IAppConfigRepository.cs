using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IAppConfigRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
    Task SetValueAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default);
    Task<BusinessProfileDto> GetBusinessProfileAsync(CancellationToken cancellationToken = default);
    Task SaveBusinessProfileAsync(BusinessProfileDto profile, CancellationToken cancellationToken = default);
    Task<RegionalSettingsDto> GetRegionalSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveRegionalSettingsAsync(RegionalSettingsDto settings, CancellationToken cancellationToken = default);
    Task<bool> HasPostedFinancialTransactionsAsync(CancellationToken cancellationToken = default);
}

