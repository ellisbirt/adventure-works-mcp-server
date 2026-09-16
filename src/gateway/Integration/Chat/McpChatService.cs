using System.Text.Json;
using EnterpriseAiGateway.Core.Mcp;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Integration.Anthropic;

namespace EnterpriseAiGateway.Integration.Chat;

public interface IMcpChatService
{
    Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default);
}

public record McpChatResult(string Message, string Tool);

public sealed class McpChatToolException(string message) : InvalidOperationException(message);

public sealed class McpChatService : IMcpChatService
{
    private const string SystemPrompt = "You answer questions about the AdventureWorks database. Use only the supplied MCP tool result. Do not infer personal information or mention excluded fields.";
    private static readonly IReadOnlyDictionary<string, object> NoArguments = new Dictionary<string, object>();
    private readonly IAnthropicClient _anthropicClient;
    private readonly IMcpToolExecutor _toolExecutor;

    public McpChatService(IAnthropicClient anthropicClient, IMcpToolExecutor toolExecutor)
    {
        _anthropicClient = anthropicClient;
        _toolExecutor = toolExecutor;
    }

    public async Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A chat message is required.", nameof(message));

        var tables = await GetSafeCatalogAsync(cancellationToken);
        var toolNames = _toolExecutor.GetToolDefinitions().Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);
        var routingPrompt = "You are an MCP tool router. Return JSON only, without markdown. " +
            "Available tools: list_database_tables, read_database_table, get_top_selling_products_summary, get_highest_revenue_products_summary. " +
            "For list_database_tables return {\"tool\":\"list_database_tables\"}. " +
            "For read_database_table return {\"tool\":\"read_database_table\",\"schema\":\"...\",\"table\":\"...\",\"limit\":1-100}. " +
            "For get_top_selling_products_summary return {\"tool\":\"get_top_selling_products_summary\",\"arguments\":{\"top\":1-100,\"startDate\":\"YYYY-MM-DD\",\"endDate\":\"YYYY-MM-DD\",\"productIds\":[1,2],\"productCategoryIds\":[1]}} with optional arguments. " +
            "For get_highest_revenue_products_summary return {\"tool\":\"get_highest_revenue_products_summary\",\"arguments\":{\"top\":1-100,\"startDate\":\"YYYY-MM-DD\",\"endDate\":\"YYYY-MM-DD\",\"productIds\":[1,2],\"productCategoryIds\":[1]}} with optional arguments. " +
            $"Only choose a schema, table, and columns from this safe MCP catalog: {JsonSerializer.Serialize(tables)}";
        var route = await _anthropicClient.SendMessageAsync(routingPrompt, message, cancellationToken);
        var selection = ParseSelection(route);
        if (!toolNames.Contains(selection.Tool))
            throw new InvalidOperationException("The model selected an unsupported MCP tool.");

        var toolResult = await ExecuteToolAsync(selection, tables, cancellationToken);
        var answer = await _anthropicClient.SendMessageAsync(
            SystemPrompt,
            $"Question: {message}\n\nMCP tool: {selection.Tool}\nMCP result: {toolResult}",
            cancellationToken);

        return new McpChatResult(answer, selection.Tool);
    }

    // Routes through the same MCP tool-dispatch executor the /mcp JSON-RPC endpoint uses, rather
    // than reading the repository directly, so chat literally exercises the MCP tool surface.
    private async Task<IReadOnlyList<SafeTableDefinition>> GetSafeCatalogAsync(CancellationToken cancellationToken)
    {
        var response = await _toolExecutor.ExecuteToolAsync("list_database_tables", NoArguments, cancellationToken);
        var text = response.Content.FirstOrDefault()?.Text ?? "[]";
        return JsonSerializer.Deserialize<List<SafeTableDefinition>>(text) ?? [];
    }

    private async Task<string> ExecuteToolAsync(McpToolSelection selection, IReadOnlyList<SafeTableDefinition> tables, CancellationToken cancellationToken)
    {
        if (selection.Tool == "list_database_tables") return JsonSerializer.Serialize(tables);

        if (selection.Tool == "read_database_table")
        {
            var schema = ReadStringArgument(selection.Arguments, "schema", selection.Schema);
            var table = ReadStringArgument(selection.Arguments, "table", selection.Table);
            var limit = ReadIntegerArgument(selection.Arguments, "limit") ?? selection.Limit ?? 20;
            if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table))
                throw new InvalidOperationException("The model selected an invalid table request.");

            var isKnownTable = tables.Any(item =>
                item.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(table, StringComparison.OrdinalIgnoreCase));
            if (!isKnownTable) throw new InvalidOperationException("The model selected a table outside the safe MCP catalog.");

            var arguments = new Dictionary<string, object>
            {
                ["schema"] = schema,
                ["table"] = table,
                ["limit"] = Math.Clamp(limit, 1, 100)
            };
            var response = await _toolExecutor.ExecuteToolAsync("read_database_table", arguments, cancellationToken);
            if (response.IsError) throw new McpChatToolException(response.Content.FirstOrDefault()?.Text ?? "The database assistant could not execute read_database_table.");
            return response.Content.FirstOrDefault()?.Text ?? string.Empty;
        }

        if (selection.Tool is not ("get_top_selling_products_summary" or "get_highest_revenue_products_summary"))
            throw new InvalidOperationException("The model selected an unsupported MCP tool.");

        var summaryResponse = await _toolExecutor.ExecuteToolAsync(selection.Tool, selection.Arguments, cancellationToken);
        if (summaryResponse.IsError) throw new McpChatToolException(summaryResponse.Content.FirstOrDefault()?.Text ?? "The database assistant could not execute the summary request.");
        return summaryResponse.Content.FirstOrDefault()?.Text ?? string.Empty;
    }

    private static string? ReadStringArgument(IReadOnlyDictionary<string, object> arguments, string key, string? fallback = null)
    {
        if (!arguments.TryGetValue(key, out var value)) return fallback;
        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String) return jsonElement.GetString();
        return value.ToString();
    }

    private static int? ReadIntegerArgument(IReadOnlyDictionary<string, object> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value)) return null;
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var integerValue)) return integerValue;
            if (jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out integerValue)) return integerValue;
            return null;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static McpToolSelection ParseSelection(string route)
    {
        using var document = JsonDocument.Parse(StripMarkdownCodeFence(route));
        var root = document.RootElement;
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal);
        if (root.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in argumentsElement.EnumerateObject())
                arguments[property.Name] = property.Value.Clone();
        }

        var schema = root.TryGetProperty("schema", out var schemaElement) ? schemaElement.GetString() : null;
        var table = root.TryGetProperty("table", out var tableElement) ? tableElement.GetString() : null;
        var limit = root.TryGetProperty("limit", out var limitElement) && limitElement.TryGetInt32(out var value) ? value : (int?)null;
        if (!string.IsNullOrWhiteSpace(schema) && !arguments.ContainsKey("schema")) arguments["schema"] = schema;
        if (!string.IsNullOrWhiteSpace(table) && !arguments.ContainsKey("table")) arguments["table"] = table;
        if (limit is not null && !arguments.ContainsKey("limit")) arguments["limit"] = limit.Value;

        return new McpToolSelection(
            root.GetProperty("tool").GetString() ?? string.Empty,
            arguments,
            schema,
            table,
            limit);
    }

    // Some models wrap JSON responses in a ```json fence despite being told not to; strip one if present.
    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;

        var withoutOpeningFence = trimmed[(firstNewline + 1)..];
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return (closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex] : withoutOpeningFence).Trim();
    }

    private record McpToolSelection(string Tool, IReadOnlyDictionary<string, object> Arguments, string? Schema, string? Table, int? Limit);
}