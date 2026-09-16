using System.Text.Json;
using System.Globalization;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Logging;
using Microsoft.Extensions.Logging;

namespace EnterpriseAiGateway.Core.Mcp;

public interface IMcpToolExecutor
{
    IReadOnlyList<McpToolDefinition> GetToolDefinitions();

    Task<McpCallToolResponse> ExecuteToolAsync(string name, IReadOnlyDictionary<string, object> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// The single MCP tool-dispatch implementation. Both the JSON-RPC /mcp endpoint and the chat
/// assistant call this executor, so a tool call behaves identically regardless of caller.
/// </summary>
public sealed class McpToolExecutor : IMcpToolExecutor
{
    private const int DefaultTop = 10;
    private const int MaxTop = 100;
    private const int MaxSalesSummaryFilterCount = 2097;
    private readonly ISecureCustomerRepository _customerRepository;
    private readonly ISecureSalesSummaryRepository _salesSummaryRepository;
    private readonly ISecureTableCatalogRepository _tableCatalog;
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(
        ISecureCustomerRepository customerRepository,
        ISecureSalesSummaryRepository salesSummaryRepository,
        ISecureTableCatalogRepository tableCatalog,
        ILogger<McpToolExecutor> logger)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _salesSummaryRepository = salesSummaryRepository ?? throw new ArgumentNullException(nameof(salesSummaryRepository));
        _tableCatalog = tableCatalog ?? throw new ArgumentNullException(nameof(tableCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<McpToolDefinition> GetToolDefinitions() =>
    [
        new("get_customer_history", "Safely reads strongly-typed historical relational summaries for a specific customer from the database schemas.", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["customerId"] = new("integer", "The unique identity key integer of the customer entity.") }, Required: ["customerId"])),
        new("list_database_tables", "Lists every database table and its useful columns available to the model; personal fields are redacted.", new McpInputSchema()),
        new("read_database_table", "Reads up to 100 rows from an available database table; personal fields are redacted and marked [REDACTED].", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["schema"] = new("string", "Schema returned by list_database_tables."), ["table"] = new("string", "Table name returned by list_database_tables."), ["limit"] = new("integer", "Optional number of rows to return, from 1 through 100.") }, Required: ["schema", "table"])),
        new("get_top_selling_products_summary", "Returns top-selling products ranked by total quantity sold, with optional date and product/category filters.", new McpInputSchema(Properties: CreateSalesSummaryInputProperties())),
        new("get_highest_revenue_products_summary", "Returns products ranked by total revenue from SalesOrderDetail.LineTotal (excluding tax/freight), with optional date and product/category filters.", new McpInputSchema(Properties: CreateSalesSummaryInputProperties()))
    ];

    public async Task<McpCallToolResponse> ExecuteToolAsync(string name, IReadOnlyDictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        if (name == "list_database_tables")
            return new([new("text", JsonSerializer.Serialize(await _tableCatalog.GetTablesAsync()))]);

        if (name == "read_database_table")
        {
            if (!arguments.TryGetValue("schema", out var rawSchema) || !arguments.TryGetValue("table", out var rawTable) ||
                string.IsNullOrWhiteSpace(rawSchema.ToString()) || string.IsNullOrWhiteSpace(rawTable.ToString()))
                return new([new("text", "Error: Missing required arguments: schema and table.")], true);

            var limit = 20;
            if (arguments.TryGetValue("limit", out var rawLimit) && (!int.TryParse(rawLimit.ToString(), out limit) || limit is < 1 or > 100))
                return new([new("text", "Error: limit must be an integer from 1 through 100.")], true);

            var rows = await _tableCatalog.GetTableRowsAsync(rawSchema.ToString()!, rawTable.ToString()!, limit);
            return new([new("text", rows)]);
        }

        if (name is "get_top_selling_products_summary" or "get_highest_revenue_products_summary")
        {
            if (!TryBuildSalesSummaryFilter(arguments, out var filter, out var error))
            {
                GatewayLogMessages.InvalidSalesSummaryArguments(_logger, name, error);
                return new([new("text", $"Error: {error}")], true);
            }

            GatewayLogMessages.SalesSummaryToolRequested(_logger, name, filter.Top, filter.StartDate is not null || filter.EndDate is not null, filter.ProductIds?.Count ?? 0, filter.ProductCategoryIds?.Count ?? 0);
            var summary = name == "get_top_selling_products_summary"
                ? await _salesSummaryRepository.GetTopSellingProductsSummaryAsync(filter, cancellationToken)
                : await _salesSummaryRepository.GetHighestRevenueProductsSummaryAsync(filter, cancellationToken);
            return new([new("text", summary)]);
        }

        if (name != "get_customer_history")
        {
            GatewayLogMessages.UnknownTool(_logger, name);
            return new([new("text", "Error: The requested tool is not mapped to this gateway server profile.")], true);
        }

        if (!arguments.TryGetValue("customerId", out var rawId) || !int.TryParse(rawId.ToString(), out var customerId) || customerId <= 0)
        {
            GatewayLogMessages.InvalidCustomerId(_logger);
            return new([new("text", "Error: Missing or malformed required argument: customerId.")], true);
        }

        GatewayLogMessages.CustomerHistoryRequested(_logger, customerId);
        return new([new("text", await _customerRepository.GetCustomerContextAsync(customerId))]);
    }

    private static Dictionary<string, McpPropertyDefinition> CreateSalesSummaryInputProperties() =>
        new()
        {
            ["top"] = new("integer", "Optional number of rows to return, from 1 through 100. Defaults to 10."),
            ["startDate"] = new("string", "Optional inclusive start date in UTC calendar format YYYY-MM-DD."),
            ["endDate"] = new("string", "Optional inclusive end date in UTC calendar format YYYY-MM-DD."),
            ["productIds"] = new("array", "Optional integer array of ProductID values to include.", new("integer", "ProductID filter value.")),
            ["productCategoryIds"] = new("array", "Optional integer array of ProductCategoryID values to include.", new("integer", "ProductCategoryID filter value."))
        };

    private static bool TryBuildSalesSummaryFilter(IReadOnlyDictionary<string, object> arguments, out SalesSummaryFilter filter, out string error)
    {
        filter = new SalesSummaryFilter(Top: DefaultTop);
        error = string.Empty;

        if (!TryReadOptionalInt(arguments, "top", out var top, out error)) return false;
        if (!TryReadOptionalDate(arguments, "startDate", out var startDate, out error)) return false;
        if (!TryReadOptionalDate(arguments, "endDate", out var endDate, out error)) return false;
        if (!TryReadOptionalIntList(arguments, "productIds", out var productIds, out error)) return false;
        if (!TryReadOptionalIntList(arguments, "productCategoryIds", out var productCategoryIds, out error)) return false;

        if (top is < 1 or > MaxTop)
        {
            error = $"top must be an integer from 1 through {MaxTop}.";
            return false;
        }

        if (startDate is not null && endDate is not null && startDate > endDate)
        {
            error = "startDate must be on or before endDate.";
            return false;
        }

        if (productIds is not null && productIds.Any(id => id <= 0))
        {
            error = "productIds must contain only positive integers.";
            return false;
        }

        if (productCategoryIds is not null && productCategoryIds.Any(id => id <= 0))
        {
            error = "productCategoryIds must contain only positive integers.";
            return false;
        }

        var combinedFilterCount = (productIds?.Count ?? 0) + (productCategoryIds?.Count ?? 0);
        if (combinedFilterCount > MaxSalesSummaryFilterCount)
        {
            error = $"productIds and productCategoryIds can contain at most {MaxSalesSummaryFilterCount} total values.";
            return false;
        }

        filter = new SalesSummaryFilter(startDate, endDate, productIds, productCategoryIds, top ?? DefaultTop);
        return true;
    }

    private static bool TryReadOptionalInt(IReadOnlyDictionary<string, object> arguments, string key, out int? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (!arguments.TryGetValue(key, out var rawValue)) return true;
        if (TryConvertInt(rawValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = $"{key} must be an integer.";
        return false;
    }

    private static bool TryReadOptionalDate(IReadOnlyDictionary<string, object> arguments, string key, out DateOnly? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (!arguments.TryGetValue(key, out var rawValue)) return true;
        if (TryConvertDate(rawValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = $"{key} must be a date in YYYY-MM-DD format.";
        return false;
    }

    private static bool TryReadOptionalIntList(IReadOnlyDictionary<string, object> arguments, string key, out IReadOnlyList<int>? values, out string error)
    {
        values = null;
        error = string.Empty;
        if (!arguments.TryGetValue(key, out var rawValue)) return true;
        if (TryConvertIntList(rawValue, out var parsed))
        {
            values = parsed;
            return true;
        }

        error = $"{key} must be an array of integers.";
        return false;
    }

    private static bool TryConvertInt(object rawValue, out int value)
    {
        if (rawValue is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)) return true;
            if (element.ValueKind == JsonValueKind.String) return int.TryParse(element.GetString(), out value);
            value = default;
            return false;
        }

        return int.TryParse(rawValue.ToString(), out value);
    }

    private static bool TryConvertDate(object rawValue, out DateOnly value)
    {
        if (rawValue is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                value = default;
                return false;
            }

            return DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        return DateOnly.TryParseExact(rawValue.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryConvertIntList(object rawValue, out IReadOnlyList<int> values)
    {
        values = [];
        if (rawValue is not JsonElement element || element.ValueKind != JsonValueKind.Array) return false;

        var parsed = new List<int>();
        foreach (var item in element.EnumerateArray())
        {
            if (!TryConvertInt(item, out var value)) return false;
            parsed.Add(value);
        }

        values = parsed;
        return true;
    }
}
