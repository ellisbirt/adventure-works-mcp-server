namespace EnterpriseAiGateway.Core.DTOs;

public sealed record SalesSummaryFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    IReadOnlyList<int>? ProductIds = null,
    IReadOnlyList<int>? ProductCategoryIds = null,
    int Top = 10);

public sealed record SalesProductSummaryRow(
    int ProductId,
    string ProductName,
    string ProductNumber,
    int? ProductCategoryId,
    string? ProductCategoryName,
    int TotalQuantitySold,
    decimal TotalRevenue,
    int OrderCount);

public sealed record SalesSummaryResponse(
    string Metric,
    string RevenueDefinition,
    SalesSummaryFilter Filters,
    IReadOnlyList<SalesProductSummaryRow> Items);
