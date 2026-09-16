using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnterpriseAiGateway.Core.DTOs;

// Payload shapes carried inside a JSON-RPC tools/list result or a tools/call result.content
// entry. The JSON-RPC envelope itself (jsonrpc/id/method/result/error) is defined further down
// in this file as McpJsonRpcRequest/McpJsonRpcResponse/McpJsonRpcError.

public record McpToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] McpInputSchema InputSchema
);

public record McpListToolsResponse(
    [property: JsonPropertyName("tools")] List<McpToolDefinition> Tools
);

public record McpInputSchema(
    [property: JsonPropertyName("type")] string Type = "object",
    [property: JsonPropertyName("properties")] Dictionary<string, McpPropertyDefinition>? Properties = null,
    [property: JsonPropertyName("required")] List<string>? Required = null
);

public record McpPropertyDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("items"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] McpPropertyDefinition? Items = null
);

public record McpCallToolRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] Dictionary<string, object> Arguments
);

public record McpCallToolResponse(
    [property: JsonPropertyName("content")] List<McpContentText> Content,
    [property: JsonPropertyName("isError")] bool IsError = false
);

public record McpContentText(
    [property: JsonPropertyName("type")] string Type = "text",
    [property: JsonPropertyName("text")] string Text = ""
);

/// <summary>
/// Id is a JsonElement, not a string/int, because JSON-RPC 2.0 permits string, number, or null
/// ids; a request with no "id" member at all is a notification (<see cref="IsNotification"/>)
/// and must never receive a response.
/// </summary>
public sealed record McpJsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string? JsonRpc,
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("params")] JsonElement Params
)
{
    public bool IsNotification => Id.ValueKind == JsonValueKind.Undefined;
}

public sealed record McpJsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] object? Data = null
);

public sealed record McpJsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] McpJsonRpcError? Error = null
);
