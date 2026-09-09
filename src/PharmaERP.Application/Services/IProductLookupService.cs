using PharmaERP.Application.DTOs;

namespace PharmaERP.Application.Services;

public interface IProductLookupService
{
    Task<ProductLookupResultDto> LookupByBarcodeOrCodeAsync(string codeOrBarcode, DateOnly? businessDate = null, CancellationToken cancellationToken = default);
    Task<List<ProductLookupResultDto>> SearchProductsAsync(string query, DateOnly? businessDate = null, int maxResults = 20, CancellationToken cancellationToken = default);
}

