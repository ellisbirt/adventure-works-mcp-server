using System.Text.Json;
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
    private readonly ISecureCustomerRepository _customerRepository;
    private readonly ISecureTableCatalogRepository _tableCatalog;
    private readonly ISecureSalesInsightsRepository _salesInsightsRepository;
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(
        ISecureCustomerRepository customerRepository,
        ISecureTableCatalogRepository tableCatalog,
        ISecureSalesInsightsRepository salesInsightsRepository,
        ILogger<McpToolExecutor> logger)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _tableCatalog = tableCatalog ?? throw new ArgumentNullException(nameof(tableCatalog));
        _salesInsightsRepository = salesInsightsRepository ?? throw new ArgumentNullException(nameof(salesInsightsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<McpToolDefinition> GetToolDefinitions() =>
    [
        new("get_customer_history", "Safely reads strongly-typed historical relational summaries for a specific customer from the database schemas.", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["customerId"] = new("integer", "The unique identity key integer of the customer entity.") }, Required: ["customerId"])),
        new("list_database_tables", "Lists every database table and its useful columns available to the model; personal fields are redacted.", new McpInputSchema()),
        new("read_database_table", "Reads up to 100 rows from an available database table; personal fields are redacted and marked [REDACTED].", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["schema"] = new("string", "Schema returned by list_database_tables."), ["table"] = new("string", "Table name returned by list_database_tables."), ["limit"] = new("integer", "Optional number of rows to return, from 1 through 100.") }, Required: ["schema", "table"])),
        new("get_product_profitability_summary", "Ranks products by estimated margin, revenue, and discount impact so the business can inspect profitability concentration.", BuildSalesInsightSchema()),
        new("get_sales_trend_summary", "Summarizes monthly revenue and order counts with period-over-period growth to detect trend changes.", BuildSalesInsightSchema()),
        new("get_customer_value_summary", "Segments repeat versus one-time customers and returns top customer lifetime value signals.", BuildSalesInsightSchema()),
        new("get_product_mix_summary", "Finds products frequently bought together to support cross-sell and upsell analysis.", BuildSalesInsightSchema()),
        new("get_geography_channel_summary", "Compares demand and shipping performance by country/state and online versus offline channel.", BuildSalesInsightSchema()),
        new("get_inventory_risk_summary", "Highlights products with demand spikes, slow movement, and sales recency risk signals.", BuildSalesInsightSchema()),
        new("get_demand_forecast_summary", "Projects next-period category revenue using recent historical trend windows.", BuildSalesInsightSchema())
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

        if (TryParseSalesInsightTool(name, arguments, out var options, out var parseError))
        {
            if (parseError is not null)
                return new([new("text", parseError)], true);

            var result = name switch
            {
                "get_product_profitability_summary" => await _salesInsightsRepository.GetProductProfitabilitySummaryAsync(options!),
                "get_sales_trend_summary" => await _salesInsightsRepository.GetSalesTrendSummaryAsync(options!),
                "get_customer_value_summary" => await _salesInsightsRepository.GetCustomerValueSummaryAsync(options!),
                "get_product_mix_summary" => await _salesInsightsRepository.GetProductMixSummaryAsync(options!),
                "get_geography_channel_summary" => await _salesInsightsRepository.GetGeographyChannelSummaryAsync(options!),
                "get_inventory_risk_summary" => await _salesInsightsRepository.GetInventoryRiskSummaryAsync(options!),
                _ => await _salesInsightsRepository.GetDemandForecastSummaryAsync(options!)
            };
            return new([new("text", result)]);
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

    private static McpInputSchema BuildSalesInsightSchema() =>
        new(Properties: new Dictionary<string, McpPropertyDefinition>
        {
            ["top"] = new("integer", "Optional number of groups to return, from 1 through 100."),
            ["startDate"] = new("string", "Optional inclusive ISO date (yyyy-MM-dd) lower bound for order date."),
            ["endDate"] = new("string", "Optional inclusive ISO date (yyyy-MM-dd) upper bound for order date."),
            ["productIds"] = new("array", "Optional product identity key filter array.", new McpPropertyItemDefinition("integer")),
            ["productCategoryIds"] = new("array", "Optional product category identity key filter array.", new McpPropertyItemDefinition("integer"))
        });

    private static bool TryParseSalesInsightTool(
        string name,
        IReadOnlyDictionary<string, object> arguments,
        out SalesInsightFilterOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        if (!name.StartsWith("get_", StringComparison.Ordinal) || !name.EndsWith("_summary", StringComparison.Ordinal))
            return false;

        var knownTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_product_profitability_summary",
            "get_sales_trend_summary",
            "get_customer_value_summary",
            "get_product_mix_summary",
            "get_geography_channel_summary",
            "get_inventory_risk_summary",
            "get_demand_forecast_summary"
        };
        if (!knownTools.Contains(name)) return false;

        var top = 10;
        if (arguments.TryGetValue("top", out var rawTop))
        {
            if (!TryGetInt(rawTop, out top) || top is < 1 or > 100)
            {
                error = "Error: top must be an integer from 1 through 100.";
                return true;
            }
        }

        if (!TryGetDate(arguments, "startDate", out var startDate, out error)) return true;
        if (!TryGetDate(arguments, "endDate", out var endDate, out error)) return true;
        if (startDate.HasValue && endDate.HasValue && startDate > endDate)
        {
            error = "Error: startDate must be less than or equal to endDate.";
            return true;
        }

        if (!TryGetIntList(arguments, "productIds", out var productIds, out error)) return true;
        if (!TryGetIntList(arguments, "productCategoryIds", out var productCategoryIds, out error)) return true;
        var totalFilterCount = (productIds?.Count ?? 0) + (productCategoryIds?.Count ?? 0);
        if (totalFilterCount > 200)
        {
            error = "Error: combined productIds and productCategoryIds filters cannot exceed 200 values.";
            return true;
        }

        options = new SalesInsightFilterOptions(top, startDate, endDate, productIds, productCategoryIds);
        return true;
    }

    private static bool TryGetDate(
        IReadOnlyDictionary<string, object> arguments,
        string key,
        out DateOnly? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!arguments.TryGetValue(key, out var raw)) return true;

        var text = raw switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => raw.ToString()
        };

        if (string.IsNullOrWhiteSpace(text) || !DateOnly.TryParse(text, out var parsed))
        {
            error = $"Error: {key} must be an ISO date string in yyyy-MM-dd format.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetIntList(
        IReadOnlyDictionary<string, object> arguments,
        string key,
        out IReadOnlyList<int>? values,
        out string? error)
    {
        values = null;
        error = null;
        if (!arguments.TryGetValue(key, out var raw)) return true;

        if (raw is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                error = $"Error: {key} must be an array of integers.";
                return false;
            }

            var parsed = new List<int>();
            foreach (var item in element.EnumerateArray())
            {
                if (!TryGetInt(item, out var number) || number <= 0)
                {
                    error = $"Error: {key} must contain positive integers only.";
                    return false;
                }

                parsed.Add(number);
            }

            values = parsed;
            return true;
        }

        error = $"Error: {key} must be an array of integers.";
        return false;
    }

    private static bool TryGetInt(object raw, out int value)
    {
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)) return true;
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value)) return true;
            value = 0;
            return false;
        }

        return int.TryParse(raw.ToString(), out value);
    }
}
