using System.Text.Json;
using EnterpriseAiGateway.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using EnterpriseAiGateway.Data.Scaffolded;

namespace EnterpriseAiGateway.Data.Repositories;

public interface ISecureSalesSummaryRepository
{
    Task<string> GetTopSellingProductsSummaryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken = default);

    Task<string> GetHighestRevenueProductsSummaryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken = default);
}

public sealed class SecureSalesSummaryRepository : ISecureSalesSummaryRepository
{
    private const string RevenueDefinition = "Revenue is calculated from SalesLT.SalesOrderDetail.LineTotal and excludes order-level tax and freight.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AdventureWorksDbContext _context;

    public SecureSalesSummaryRepository(AdventureWorksDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string> GetTopSellingProductsSummaryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken = default)
    {
        var normalizedFilter = NormalizeFilter(filter);
        var summaries = await BuildSummaryRowsQuery(normalizedFilter)
            .OrderByDescending(item => item.TotalQuantitySold)
            .ThenByDescending(item => item.TotalRevenue)
            .ThenBy(item => item.ProductId)
            .Take(normalizedFilter.Top)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(
            new SalesSummaryResponse("top_selling_products_by_quantity", RevenueDefinition, normalizedFilter, summaries),
            JsonOptions);
    }

    public async Task<string> GetHighestRevenueProductsSummaryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken = default)
    {
        var normalizedFilter = NormalizeFilter(filter);
        var summaries = await BuildSummaryRowsQuery(normalizedFilter)
            .OrderByDescending(item => item.TotalRevenue)
            .ThenByDescending(item => item.TotalQuantitySold)
            .ThenBy(item => item.ProductId)
            .Take(normalizedFilter.Top)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(
            new SalesSummaryResponse("highest_revenue_products", RevenueDefinition, normalizedFilter, summaries),
            JsonOptions);
    }

    private IQueryable<SalesProductSummaryRow> BuildSummaryRowsQuery(SalesSummaryFilter filter)
    {
        var query =
            from detail in _context.SalesOrderDetails.AsNoTracking()
            join header in _context.SalesOrderHeaders.AsNoTracking() on detail.SalesOrderId equals header.SalesOrderId
            join product in _context.Products.AsNoTracking() on detail.ProductId equals product.ProductId
            join category in _context.ProductCategories.AsNoTracking() on product.ProductCategoryId equals category.ProductCategoryId into categoryGroup
            from category in categoryGroup.DefaultIfEmpty()
            select new
            {
                header.OrderDate,
                detail.SalesOrderId,
                detail.OrderQty,
                detail.LineTotal,
                product.ProductId,
                product.Name,
                product.ProductNumber,
                product.ProductCategoryId,
                CategoryName = category == null ? null : category.Name
            };

        if (filter.StartDate is not null)
        {
            var startUtcInclusive = filter.StartDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => item.OrderDate >= startUtcInclusive);
        }

        if (filter.EndDate is not null)
        {
            var endUtcInclusive = filter.EndDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(item => item.OrderDate <= endUtcInclusive);
        }

        if (filter.ProductIds is { Count: > 0 })
            query = query.Where(item => filter.ProductIds.Contains(item.ProductId));

        if (filter.ProductCategoryIds is { Count: > 0 })
            query = query.Where(item => item.ProductCategoryId.HasValue && filter.ProductCategoryIds.Contains(item.ProductCategoryId.Value));

        return query
            .GroupBy(item => new
            {
                item.ProductId,
                item.Name,
                item.ProductNumber,
                item.ProductCategoryId,
                item.CategoryName
            })
            .Select(group => new SalesProductSummaryRow(
                group.Key.ProductId,
                group.Key.Name,
                group.Key.ProductNumber,
                group.Key.ProductCategoryId,
                group.Key.CategoryName,
                group.Sum(item => (int)item.OrderQty),
                group.Sum(item => item.LineTotal),
                group.Select(item => item.SalesOrderId).Distinct().Count()));
    }

    internal static SalesSummaryFilter NormalizeFilter(SalesSummaryFilter filter)
    {
        var validProductIds = (filter.ProductIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var validProductCategoryIds = (filter.ProductCategoryIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var top = Math.Clamp(filter.Top <= 0 ? 10 : filter.Top, 1, 100);
        return new SalesSummaryFilter(filter.StartDate, filter.EndDate, validProductIds, validProductCategoryIds, top);
    }
}
