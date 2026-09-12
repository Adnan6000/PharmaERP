namespace PharmaERP.Application.DTOs;

public record LookupDto(int Id, string Name, string? Code = null)
{
    public override string ToString() => Name;
}

public record ProductLookupsDto(
    IReadOnlyList<LookupDto> Manufacturers,
    IReadOnlyList<LookupDto> Categories,
    IReadOnlyList<LookupDto> Units);

public record DashboardMetricsDto(
    int TotalProducts,
    int ActiveProducts,
    int TotalManufacturers,
    int TotalCategories,
    int TotalUnits,
    decimal TodaySalesTotal = 0m,
    int TodayInvoiceCount = 0,
    decimal TodayReturnsTotal = 0m,
    int TodayReturnCount = 0,
    decimal TodayNetSales = 0m);

