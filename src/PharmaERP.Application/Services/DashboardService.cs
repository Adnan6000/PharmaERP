using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public interface IDashboardService
{
    Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
}

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboardRepository;

    public DashboardService(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await _dashboardRepository.GetMetricsAsync(cancellationToken);
    }
}

