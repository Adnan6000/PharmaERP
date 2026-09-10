using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IHistoricalBackfillRunner
{
    Task<HistoricalInitializationResultDto> ExecuteBackfillAsync(CancellationToken cancellationToken = default);
}
