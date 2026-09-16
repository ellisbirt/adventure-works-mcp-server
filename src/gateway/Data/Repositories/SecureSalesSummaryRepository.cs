using System.Data;
using System.Data.Common;
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
        var summaries = await BuildSummariesAsync(normalizedFilter, rankByRevenue: false, cancellationToken);

        return JsonSerializer.Serialize(
            new SalesSummaryResponse("top_selling_products_by_quantity", RevenueDefinition, normalizedFilter, summaries),
            JsonOptions);
    }

    public async Task<string> GetHighestRevenueProductsSummaryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken = default)
    {
        var normalizedFilter = NormalizeFilter(filter);
        var summaries = await BuildSummariesAsync(normalizedFilter, rankByRevenue: true, cancellationToken);

        return JsonSerializer.Serialize(
            new SalesSummaryResponse("highest_revenue_products", RevenueDefinition, normalizedFilter, summaries),
            JsonOptions);
    }

    private async Task<IReadOnlyList<SalesProductSummaryRow>> BuildSummariesAsync(SalesSummaryFilter filter, bool rankByRevenue, CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
            return await BuildSummaryRowsRelationalAsync(filter, rankByRevenue, cancellationToken);

        var inMemoryRows = await BuildSummaryRowsInMemoryAsync(filter, cancellationToken);
        return inMemoryRows
            .OrderByDescending(item => rankByRevenue ? item.TotalRevenue : item.TotalQuantitySold)
            .ThenByDescending(item => rankByRevenue ? item.TotalQuantitySold : item.TotalRevenue)
            .ThenBy(item => item.ProductId)
            .Take(filter.Top)
            .Select(ToSalesProductSummaryRow)
            .ToList();
    }

    private async Task<IReadOnlyList<SalesSummaryAggregate>> BuildSummaryRowsInMemoryAsync(SalesSummaryFilter filter, CancellationToken cancellationToken)
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

        var filteredRows = await query
            .Select(item => new
            {
                item.SalesOrderId,
                item.OrderQty,
                item.LineTotal,
                item.ProductId,
                ProductName = item.Name,
                item.ProductNumber,
                item.ProductCategoryId,
                ProductCategoryName = item.CategoryName
            })
            .ToListAsync(cancellationToken);

        return filteredRows
            .GroupBy(item => new
            {
                item.ProductId,
                item.ProductName,
                item.ProductNumber,
                item.ProductCategoryId,
                item.ProductCategoryName
            })
            .Select(group => new SalesSummaryAggregate(
                group.Key.ProductId,
                group.Key.ProductName,
                group.Key.ProductNumber,
                group.Key.ProductCategoryId,
                group.Key.ProductCategoryName,
                group.Sum(item => (int)item.OrderQty),
                group.Sum(item => item.LineTotal),
                group.Select(item => item.SalesOrderId).Distinct().Count()))
            .ToList();
    }

    private async Task<IReadOnlyList<SalesProductSummaryRow>> BuildSummaryRowsRelationalAsync(SalesSummaryFilter filter, bool rankByRevenue, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var whereClauses = new List<string>();
        if (filter.StartDate is not null)
        {
            whereClauses.Add("h.OrderDate >= @startDate");
            AddParameter(command, "@startDate", filter.StartDate.Value.ToDateTime(TimeOnly.MinValue), DbType.DateTime2);
        }

        if (filter.EndDate is not null)
        {
            whereClauses.Add("h.OrderDate <= @endDate");
            AddParameter(command, "@endDate", filter.EndDate.Value.ToDateTime(TimeOnly.MaxValue), DbType.DateTime2);
        }

        AddInClause(command, whereClauses, "d.ProductID", "@productId", filter.ProductIds);
        AddInClause(command, whereClauses, "p.ProductCategoryID", "@productCategoryId", filter.ProductCategoryIds);

        AddParameter(command, "@top", filter.Top, DbType.Int32);
        var whereSql = whereClauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereClauses)}";
        var orderSql = rankByRevenue
            ? "ORDER BY TotalRevenue DESC, TotalQuantitySold DESC, d.ProductID ASC"
            : "ORDER BY TotalQuantitySold DESC, TotalRevenue DESC, d.ProductID ASC";

        command.CommandText = $"""
            SELECT TOP (@top)
                d.ProductID AS ProductId,
                p.Name AS ProductName,
                p.ProductNumber AS ProductNumber,
                p.ProductCategoryID AS ProductCategoryId,
                pc.Name AS ProductCategoryName,
                SUM(CAST(d.OrderQty AS int)) AS TotalQuantitySold,
                SUM(d.LineTotal) AS TotalRevenue,
                COUNT(DISTINCT d.SalesOrderID) AS OrderCount
            FROM SalesLT.SalesOrderDetail d
            JOIN SalesLT.SalesOrderHeader h ON h.SalesOrderID = d.SalesOrderID
            JOIN SalesLT.Product p ON p.ProductID = d.ProductID
            LEFT JOIN SalesLT.ProductCategory pc ON pc.ProductCategoryID = p.ProductCategoryID
            {whereSql}
            GROUP BY d.ProductID, p.Name, p.ProductNumber, p.ProductCategoryID, pc.Name
            {orderSql};
            """;

        var rows = new List<SalesProductSummaryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SalesProductSummaryRow(
                ProductId: reader.GetInt32(0),
                ProductName: reader.GetString(1),
                ProductNumber: reader.GetString(2),
                ProductCategoryId: await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetInt32(3),
                ProductCategoryName: await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                TotalQuantitySold: reader.GetInt32(5),
                TotalRevenue: reader.GetDecimal(6),
                OrderCount: reader.GetInt32(7)));
        }

        return rows;
    }

    internal static SalesSummaryFilter NormalizeFilter(SalesSummaryFilter filter)
    {
        var validProductIds = (filter.ProductIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var validProductCategoryIds = (filter.ProductCategoryIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var top = Math.Clamp(filter.Top <= 0 ? 10 : filter.Top, 1, 100);
        return new SalesSummaryFilter(filter.StartDate, filter.EndDate, validProductIds, validProductCategoryIds, top);
    }

    private static SalesProductSummaryRow ToSalesProductSummaryRow(SalesSummaryAggregate item) =>
        new(
            item.ProductId,
            item.ProductName,
            item.ProductNumber,
            item.ProductCategoryId,
            item.ProductCategoryName,
            item.TotalQuantitySold,
            item.TotalRevenue,
            item.OrderCount);

    private static void AddParameter(DbCommand command, string name, object value, DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddInClause(DbCommand command, List<string> whereClauses, string columnSql, string parameterPrefix, IReadOnlyList<int>? values)
    {
        if (values is null || values.Count == 0) return;

        var parameterNames = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            var parameterName = $"{parameterPrefix}{index}";
            parameterNames.Add(parameterName);
            AddParameter(command, parameterName, values[index], DbType.Int32);
        }

        whereClauses.Add($"{columnSql} IN ({string.Join(", ", parameterNames)})");
    }

    private sealed record SalesSummaryAggregate(
        int ProductId,
        string ProductName,
        string ProductNumber,
        int? ProductCategoryId,
        string? ProductCategoryName,
        int TotalQuantitySold,
        decimal TotalRevenue,
        int OrderCount);
}
