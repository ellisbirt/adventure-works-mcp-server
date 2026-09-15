using System.Text.Json;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Integration.Anthropic;

namespace EnterpriseAiGateway.Integration.Chat;

public interface IMcpChatService
{
    Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default);
}

public record McpChatResult(string Message, string Tool);

public sealed class McpChatService : IMcpChatService
{
    private const string SystemPrompt = "You answer questions about the AdventureWorks database. Use only the supplied MCP tool result. Do not infer personal information or mention excluded fields.";
    private readonly IAnthropicClient _anthropicClient;
    private readonly ISecureTableCatalogRepository _tableCatalog;

    public McpChatService(IAnthropicClient anthropicClient, ISecureTableCatalogRepository tableCatalog)
    {
        _anthropicClient = anthropicClient;
        _tableCatalog = tableCatalog;
    }

    public async Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A chat message is required.", nameof(message));

        var tables = await _tableCatalog.GetTablesAsync();
        var routingPrompt = "You are an MCP tool router. Return JSON only, without markdown. " +
            "Available tools: list_database_tables, read_database_table. " +
            "For list_database_tables return {\"tool\":\"list_database_tables\"}. " +
            "For read_database_table return {\"tool\":\"read_database_table\",\"schema\":\"...\",\"table\":\"...\",\"limit\":1-100}. " +
            $"Only choose a schema, table, and columns from this safe MCP catalog: {JsonSerializer.Serialize(tables)}";
        var route = await _anthropicClient.SendMessageAsync(routingPrompt, message, cancellationToken);
        var selection = ParseSelection(route);
        var toolResult = await ExecuteToolAsync(selection, tables);
        var answer = await _anthropicClient.SendMessageAsync(
            SystemPrompt,
            $"Question: {message}\n\nMCP tool: {selection.Tool}\nMCP result: {toolResult}",
            cancellationToken);

        return new McpChatResult(answer, selection.Tool);
    }

    private async Task<string> ExecuteToolAsync(McpToolSelection selection, IReadOnlyList<SafeTableDefinition> tables)
    {
        if (selection.Tool == "list_database_tables") return JsonSerializer.Serialize(tables);

        if (selection.Tool != "read_database_table" || string.IsNullOrWhiteSpace(selection.Schema) || string.IsNullOrWhiteSpace(selection.Table))
            throw new InvalidOperationException("The model selected an unsupported MCP tool.");

        var isKnownTable = tables.Any(item =>
            item.Schema.Equals(selection.Schema, StringComparison.OrdinalIgnoreCase) &&
            item.Name.Equals(selection.Table, StringComparison.OrdinalIgnoreCase));
        if (!isKnownTable) throw new InvalidOperationException("The model selected a table outside the safe MCP catalog.");

        return await _tableCatalog.GetTableRowsAsync(selection.Schema, selection.Table, Math.Clamp(selection.Limit ?? 20, 1, 100));
    }

    private static McpToolSelection ParseSelection(string route)
    {
        using var document = JsonDocument.Parse(route);
        var root = document.RootElement;
        return new McpToolSelection(
            root.GetProperty("tool").GetString() ?? string.Empty,
            root.TryGetProperty("schema", out var schema) ? schema.GetString() : null,
            root.TryGetProperty("table", out var table) ? table.GetString() : null,
            root.TryGetProperty("limit", out var limit) && limit.TryGetInt32(out var value) ? value : null);
    }

    private record McpToolSelection(string Tool, string? Schema, string? Table, int? Limit);
}