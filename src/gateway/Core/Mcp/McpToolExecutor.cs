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
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(
        ISecureCustomerRepository customerRepository,
        ISecureTableCatalogRepository tableCatalog,
        ILogger<McpToolExecutor> logger)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _tableCatalog = tableCatalog ?? throw new ArgumentNullException(nameof(tableCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<McpToolDefinition> GetToolDefinitions() =>
    [
        new("get_customer_history", "Safely reads strongly-typed historical relational summaries for a specific customer from the database schemas.", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["customerId"] = new("integer", "The unique identity key integer of the customer entity.") }, Required: ["customerId"])),
        new("list_database_tables", "Lists every database table and its useful columns available to the model; personal fields are redacted.", new McpInputSchema()),
        new("read_database_table", "Reads up to 100 rows from an available database table; personal fields are redacted and marked [REDACTED].", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["schema"] = new("string", "Schema returned by list_database_tables."), ["table"] = new("string", "Table name returned by list_database_tables."), ["limit"] = new("integer", "Optional number of rows to return, from 1 through 100.") }, Required: ["schema", "table"]))
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
}
