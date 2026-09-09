using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Common.Interfaces;

public interface IInventoryTransactionWriter
{
    Task<ProductBatchDto> RecordOpeningStockAsync(
        OpeningStockCreateDto dto,
        CancellationToken cancellationToken = default);
}

