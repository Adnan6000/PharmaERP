using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IDashboardRepository
{
    Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
}

