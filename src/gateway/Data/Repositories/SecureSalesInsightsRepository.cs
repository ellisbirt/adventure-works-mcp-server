using System.Text.Json;
using EnterpriseAiGateway.Data.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAiGateway.Data.Repositories;

public sealed record SalesInsightFilterOptions(
    int Top = 10,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    IReadOnlyList<int>? ProductIds = null,
    IReadOnlyList<int>? ProductCategoryIds = null);

public interface ISecureSalesInsightsRepository
{
    Task<string> GetProductProfitabilitySummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetSalesTrendSummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetCustomerValueSummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetProductMixSummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetGeographyChannelSummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetInventoryRiskSummaryAsync(SalesInsightFilterOptions options);

    Task<string> GetDemandForecastSummaryAsync(SalesInsightFilterOptions options);
}

public sealed class SecureSalesInsightsRepository : ISecureSalesInsightsRepository
{
    private readonly AdventureWorksDbContext _context;

    public SecureSalesInsightsRepository(AdventureWorksDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string> GetProductProfitabilitySummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var lines = ApplyFilters(BuildSalesLineQuery(), options);

        var summary = await lines
            .GroupBy(line => new { line.ProductId, line.ProductName, line.ProductCategoryId })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.ProductName,
                group.Key.ProductCategoryId,
                Revenue = group.Sum(item => item.LineTotal),
                EstimatedCost = group.Sum(item => item.StandardCost * item.OrderQty),
                DiscountValue = group.Sum(item => item.UnitPrice * item.OrderQty * item.UnitPriceDiscount),
                OrderLines = group.Count()
            })
            .OrderByDescending(item => item.Revenue - item.EstimatedCost)
            .ThenByDescending(item => item.Revenue)
            .Take(top)
            .ToListAsync();

        var payload = summary.Select(item => new
        {
            item.ProductId,
            item.ProductName,
            item.ProductCategoryId,
            item.Revenue,
            item.EstimatedCost,
            EstimatedMargin = item.Revenue - item.EstimatedCost,
            MarginPercent = item.Revenue == 0 ? 0 : decimal.Round(((item.Revenue - item.EstimatedCost) / item.Revenue) * 100, 2),
            item.DiscountValue,
            item.OrderLines
        });

        return JsonSerializer.Serialize(payload);
    }

    public async Task<string> GetSalesTrendSummaryAsync(SalesInsightFilterOptions options)
    {
        var lines = ApplyFilters(BuildSalesLineQuery(), options);
        var monthly = await lines
            .GroupBy(line => new { line.OrderDate.Year, line.OrderDate.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Revenue = group.Sum(item => item.LineTotal),
                Orders = group.Select(item => item.SalesOrderId).Distinct().Count()
            })
            .OrderBy(item => item.Year)
            .ThenBy(item => item.Month)
            .ToListAsync();

        decimal? previousRevenue = null;
        var payload = monthly.Select(item =>
        {
            var growthPercent = previousRevenue.HasValue && previousRevenue.Value != 0
                ? decimal.Round(((item.Revenue - previousRevenue.Value) / previousRevenue.Value) * 100, 2)
                : (decimal?)null;
            previousRevenue = item.Revenue;
            return new
            {
                Period = $"{item.Year:D4}-{item.Month:D2}",
                item.Revenue,
                item.Orders,
                RevenueGrowthPercent = growthPercent
            };
        });

        return JsonSerializer.Serialize(payload);
    }

    public async Task<string> GetCustomerValueSummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var lines = ApplyFilters(BuildSalesLineQuery(), options);
        var customerMetrics = await lines
            .GroupBy(line => line.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Orders = group.Select(item => item.SalesOrderId).Distinct().Count(),
                TotalSpend = group.Sum(item => item.LineTotal)
            })
            .ToListAsync();

        var segments = new
        {
            RepeatCustomers = customerMetrics.Count(item => item.Orders > 1),
            OneTimeCustomers = customerMetrics.Count(item => item.Orders == 1),
            TotalCustomers = customerMetrics.Count
        };

        var topCustomers = customerMetrics
            .OrderByDescending(item => item.TotalSpend)
            .Take(top)
            .Select(item => new
            {
                item.CustomerId,
                item.Orders,
                item.TotalSpend,
                AverageOrderValue = item.Orders == 0 ? 0 : decimal.Round(item.TotalSpend / item.Orders, 2),
                Segment = item.Orders > 1 ? "repeat" : "one_time"
            });

        return JsonSerializer.Serialize(new { segments, topCustomers });
    }

    public async Task<string> GetProductMixSummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var distinctLines = await ApplyFilters(BuildSalesLineQuery(), options)
            .Select(line => new { line.SalesOrderId, line.ProductId, line.ProductName })
            .Distinct()
            .ToListAsync();

        var groupedByOrder = distinctLines.GroupBy(item => item.SalesOrderId);
        var pairCounts = new Dictionary<(int FirstProductId, int SecondProductId), int>();
        var productNames = distinctLines
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First().ProductName);

        foreach (var order in groupedByOrder)
        {
            var products = order.Select(item => item.ProductId).Distinct().OrderBy(id => id).ToArray();
            for (var index = 0; index < products.Length; index++)
            {
                for (var other = index + 1; other < products.Length; other++)
                {
                    var key = (products[index], products[other]);
                    pairCounts.TryGetValue(key, out var current);
                    pairCounts[key] = current + 1;
                }
            }
        }

        var payload = pairCounts
            .OrderByDescending(item => item.Value)
            .Take(top)
            .Select(item => new
            {
                FirstProductId = item.Key.FirstProductId,
                FirstProductName = productNames[item.Key.FirstProductId],
                SecondProductId = item.Key.SecondProductId,
                SecondProductName = productNames[item.Key.SecondProductId],
                OrdersTogether = item.Value
            });

        return JsonSerializer.Serialize(payload);
    }

    public async Task<string> GetGeographyChannelSummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var filteredOrderIds = await ApplyFilters(BuildSalesLineQuery(), options)
            .Select(line => line.SalesOrderId)
            .Distinct()
            .ToListAsync();

        var orderSet = filteredOrderIds.ToHashSet();
        var headers = await _context.SalesOrderHeaders
            .AsNoTracking()
            .Where(header => orderSet.Contains(header.SalesOrderId))
            .Select(header => new
            {
                header.SalesOrderId,
                header.OrderDate,
                header.DueDate,
                header.ShipDate,
                header.OnlineOrderFlag,
                header.TotalDue,
                Address = header.ShipToAddress == null ? null : new
                {
                    header.ShipToAddress.CountryRegion,
                    header.ShipToAddress.StateProvince
                }
            })
            .ToListAsync();

        var payload = headers
            .GroupBy(header => new
            {
                CountryRegion = header.Address?.CountryRegion ?? "Unknown",
                StateProvince = header.Address?.StateProvince ?? "Unknown",
                Channel = header.OnlineOrderFlag ? "online" : "offline"
            })
            .Select(group => new
            {
                group.Key.CountryRegion,
                group.Key.StateProvince,
                group.Key.Channel,
                Orders = group.Count(),
                Revenue = group.Sum(item => item.TotalDue),
                AverageShippingDays = decimal.Round((decimal)group.Where(item => item.ShipDate.HasValue).Select(item => (item.ShipDate!.Value - item.OrderDate).TotalDays).DefaultIfEmpty(0).Average(), 2),
                LateShipmentRatePercent = decimal.Round(group.Count() == 0 ? 0 : (decimal)group.Count(item => !item.ShipDate.HasValue || item.ShipDate > item.DueDate) / group.Count() * 100, 2)
            })
            .OrderByDescending(item => item.Revenue)
            .Take(top);

        return JsonSerializer.Serialize(payload);
    }

    public async Task<string> GetInventoryRiskSummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var lines = ApplyFilters(BuildSalesLineQuery(), options);
        var now = options.EndDate?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.UtcNow;
        var recentStart = now.AddDays(-30);
        var previousStart = now.AddDays(-60);

        var summary = await lines
            .GroupBy(line => new { line.ProductId, line.ProductName, line.ProductCategoryId })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.ProductName,
                group.Key.ProductCategoryId,
                TotalQuantity = group.Sum(item => item.OrderQty),
                RecentQuantity = group.Where(item => item.OrderDate >= recentStart).Sum(item => item.OrderQty),
                PreviousQuantity = group.Where(item => item.OrderDate >= previousStart && item.OrderDate < recentStart).Sum(item => item.OrderQty),
                LastSaleDate = group.Max(item => item.OrderDate)
            })
            .OrderByDescending(item => item.TotalQuantity)
            .Take(top)
            .ToListAsync();

        var payload = summary.Select(item =>
        {
            var trend = item.PreviousQuantity == 0
                ? (decimal?)null
                : decimal.Round(((item.RecentQuantity - item.PreviousQuantity) / (decimal)item.PreviousQuantity) * 100, 2);
            var riskSignal = trend switch
            {
                > 35 => "demand_spike",
                < -35 => "slow_movement",
                _ => "stable"
            };

            return new
            {
                item.ProductId,
                item.ProductName,
                item.ProductCategoryId,
                item.TotalQuantity,
                item.RecentQuantity,
                item.PreviousQuantity,
                RecentTrendPercent = trend,
                DaysSinceLastSale = (int)Math.Max(0, (now - item.LastSaleDate).TotalDays),
                RiskSignal = riskSignal
            };
        });

        return JsonSerializer.Serialize(payload);
    }

    public async Task<string> GetDemandForecastSummaryAsync(SalesInsightFilterOptions options)
    {
        var top = ClampTop(options.Top);
        var monthly = await ApplyFilters(BuildSalesLineQuery(), options)
            .GroupBy(line => new { line.ProductCategoryId, line.OrderDate.Year, line.OrderDate.Month })
            .Select(group => new
            {
                group.Key.ProductCategoryId,
                group.Key.Year,
                group.Key.Month,
                Revenue = group.Sum(item => item.LineTotal)
            })
            .ToListAsync();

        var forecast = monthly
            .GroupBy(item => item.ProductCategoryId)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.Year).ThenBy(item => item.Month).ToList();
                var trailing = ordered.TakeLast(3).ToList();
                var projectedRevenue = trailing.Count == 0 ? 0 : decimal.Round(trailing.Average(item => item.Revenue), 2);
                var last = trailing.LastOrDefault();
                var previous = trailing.Count > 1 ? trailing[^2] : null;
                var growthPercent = previous is null || previous.Revenue == 0
                    ? (decimal?)null
                    : decimal.Round(((last!.Revenue - previous.Revenue) / previous.Revenue) * 100, 2);
                return new
                {
                    ProductCategoryId = group.Key,
                    ProjectedNextPeriodRevenue = projectedRevenue,
                    CurrentPeriodRevenue = last?.Revenue ?? 0,
                    RecentGrowthPercent = growthPercent,
                    Samples = trailing.Count
                };
            })
            .OrderByDescending(item => item.ProjectedNextPeriodRevenue)
            .Take(top)
            .ToList();

        var categoryNames = await _context.ProductCategories.AsNoTracking()
            .ToDictionaryAsync(category => category.ProductCategoryId, category => category.Name);

        var payload = forecast.Select(item => new
        {
            item.ProductCategoryId,
            ProductCategoryName = item.ProductCategoryId.HasValue && categoryNames.TryGetValue(item.ProductCategoryId.Value, out var categoryName)
                ? categoryName
                : "Uncategorized",
            item.ProjectedNextPeriodRevenue,
            item.CurrentPeriodRevenue,
            item.RecentGrowthPercent,
            item.Samples
        });

        return JsonSerializer.Serialize(payload);
    }

    private IQueryable<SalesLineProjection> BuildSalesLineQuery() =>
        from detail in _context.SalesOrderDetails.AsNoTracking()
        join header in _context.SalesOrderHeaders.AsNoTracking() on detail.SalesOrderId equals header.SalesOrderId
        join product in _context.Products.AsNoTracking() on detail.ProductId equals product.ProductId
        select new SalesLineProjection(
            detail.SalesOrderId,
            header.OrderDate,
            header.DueDate,
            header.ShipDate,
            header.CustomerId,
            header.OnlineOrderFlag,
            product.ProductId,
            product.Name,
            product.ProductCategoryId,
            detail.OrderQty,
            detail.UnitPrice,
            detail.UnitPriceDiscount,
            detail.LineTotal,
            product.StandardCost);

    private static IQueryable<SalesLineProjection> ApplyFilters(IQueryable<SalesLineProjection> query, SalesInsightFilterOptions options)
    {
        var start = options.StartDate?.ToDateTime(TimeOnly.MinValue);
        var end = options.EndDate?.ToDateTime(TimeOnly.MaxValue);

        if (start.HasValue) query = query.Where(item => item.OrderDate >= start.Value);
        if (end.HasValue) query = query.Where(item => item.OrderDate <= end.Value);
        if (options.ProductIds?.Count > 0)
        {
            var productIds = options.ProductIds.Distinct().ToArray();
            query = query.Where(item => productIds.Contains(item.ProductId));
        }

        if (options.ProductCategoryIds?.Count > 0)
        {
            var categoryIds = options.ProductCategoryIds.Distinct().ToArray();
            query = query.Where(item => item.ProductCategoryId.HasValue && categoryIds.Contains(item.ProductCategoryId.Value));
        }

        return query;
    }

    private static int ClampTop(int top) => Math.Clamp(top, 1, 100);

    private sealed record SalesLineProjection(
        int SalesOrderId,
        DateTime OrderDate,
        DateTime DueDate,
        DateTime? ShipDate,
        int CustomerId,
        bool OnlineOrderFlag,
        int ProductId,
        string ProductName,
        int? ProductCategoryId,
        short OrderQty,
        decimal UnitPrice,
        decimal UnitPriceDiscount,
        decimal LineTotal,
        decimal StandardCost);
}
