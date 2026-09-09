using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;

namespace PharmaERP.Infrastructure.Persistence.Repositories;

public class AppConfigRepository : IAppConfigRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<AppConfigRepository> _logger;

    public AppConfigRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<AppConfigRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await context.AppConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        return config?.Value;
    }

    public async Task SetValueAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await context.AppConfigs
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken);

        if (config == null)
        {
            config = new AppConfig
            {
                Key = key,
                Value = value,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow
            };
            await context.AppConfigs.AddAsync(config, cancellationToken);
        }
        else
        {
            config.Value = value;
            if (description != null) config.Description = description;
            config.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<BusinessProfileDto> GetBusinessProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var configs = await context.AppConfigs
            .AsNoTracking()
            .Where(c => c.Key.StartsWith("BusinessProfile."))
            .ToDictionaryAsync(c => c.Key, c => c.Value, cancellationToken);

        var profile = new BusinessProfileDto();
        if (configs.TryGetValue("BusinessProfile.PharmacyName", out var name) && !string.IsNullOrWhiteSpace(name))
            profile.PharmacyName = name;
        if (configs.TryGetValue("BusinessProfile.Address", out var addr) && !string.IsNullOrWhiteSpace(addr))
            profile.Address = addr;
        if (configs.TryGetValue("BusinessProfile.Phone", out var phone) && !string.IsNullOrWhiteSpace(phone))
            profile.Phone = phone;
        if (configs.TryGetValue("BusinessProfile.DrugLicenseNumber", out var lic) && !string.IsNullOrWhiteSpace(lic))
            profile.DrugLicenseNumber = lic;
        if (configs.TryGetValue("BusinessProfile.TaxNumber", out var tax) && !string.IsNullOrWhiteSpace(tax))
            profile.TaxNumber = tax;
        if (configs.TryGetValue("BusinessProfile.ReceiptFooter", out var footer) && !string.IsNullOrWhiteSpace(footer))
            profile.ReceiptFooter = footer;
        if (configs.TryGetValue("BusinessProfile.TimeZoneId", out var tz) && !string.IsNullOrWhiteSpace(tz))
            profile.TimeZoneId = tz;

        return profile;
    }

    public async Task SaveBusinessProfileAsync(BusinessProfileDto profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await SetValueAsync("BusinessProfile.PharmacyName", profile.PharmacyName, "Pharmacy display name", cancellationToken);
        await SetValueAsync("BusinessProfile.Address", profile.Address, "Pharmacy physical address", cancellationToken);
        await SetValueAsync("BusinessProfile.Phone", profile.Phone, "Pharmacy contact phone", cancellationToken);
        await SetValueAsync("BusinessProfile.DrugLicenseNumber", profile.DrugLicenseNumber, "Pharmacy drug license number", cancellationToken);
        await SetValueAsync("BusinessProfile.TaxNumber", profile.TaxNumber, "Pharmacy tax / NTN number", cancellationToken);
        await SetValueAsync("BusinessProfile.ReceiptFooter", profile.ReceiptFooter, "Default footer message on printed receipts", cancellationToken);
        await SetValueAsync("BusinessProfile.TimeZoneId", profile.TimeZoneId, "Business timezone identifier", cancellationToken);
    }
}

