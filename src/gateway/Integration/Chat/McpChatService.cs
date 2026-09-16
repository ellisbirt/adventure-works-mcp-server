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
        var routingPrompt = "You are an MCP tool router. Return JSON only, without markdown. " +
            "Available tools: list_database_tables, read_database_table. " +
            "For list_database_tables return {\"tool\":\"list_database_tables\"}. " +
            "For read_database_table return {\"tool\":\"read_database_table\",\"schema\":\"...\",\"table\":\"...\",\"limit\":1-100}. " +
            $"Only choose a schema, table, and columns from this safe MCP catalog: {JsonSerializer.Serialize(tables)}";
        var route = await _anthropicClient.SendMessageAsync(routingPrompt, message, cancellationToken);
        var selection = ParseSelection(route);
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

        if (selection.Tool != "read_database_table" || string.IsNullOrWhiteSpace(selection.Schema) || string.IsNullOrWhiteSpace(selection.Table))
            throw new InvalidOperationException("The model selected an unsupported MCP tool.");

        var isKnownTable = tables.Any(item =>
            item.Schema.Equals(selection.Schema, StringComparison.OrdinalIgnoreCase) &&
            item.Name.Equals(selection.Table, StringComparison.OrdinalIgnoreCase));
        if (!isKnownTable) throw new InvalidOperationException("The model selected a table outside the safe MCP catalog.");

        var arguments = new Dictionary<string, object>
        {
            ["schema"] = selection.Schema,
            ["table"] = selection.Table,
            ["limit"] = Math.Clamp(selection.Limit ?? 20, 1, 100)
        };
        var response = await _toolExecutor.ExecuteToolAsync("read_database_table", arguments, cancellationToken);
        if (response.IsError) throw new InvalidOperationException(response.Content.FirstOrDefault()?.Text ?? "The selected MCP tool returned an error.");
        return response.Content.FirstOrDefault()?.Text ?? string.Empty;
    }

    private static McpToolSelection ParseSelection(string route)
    {
        using var document = JsonDocument.Parse(StripMarkdownCodeFence(route));
        var root = document.RootElement;
        return new McpToolSelection(
            root.GetProperty("tool").GetString() ?? string.Empty,
            root.TryGetProperty("schema", out var schema) ? schema.GetString() : null,
            root.TryGetProperty("table", out var table) ? table.GetString() : null,
            root.TryGetProperty("limit", out var limit) && limit.TryGetInt32(out var value) ? value : null);
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

    private record McpToolSelection(string Tool, string? Schema, string? Table, int? Limit);
}