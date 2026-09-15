// src/gateway/Core/DTOs/McpContracts.cs
using System.Text.Json.Serialization;

namespace EnterpriseAiGateway.Core.DTOs;

/// <summary>
/// Strongly-typed C# records for the gateway's MCP-shaped REST compatibility contract.
/// These records are not the MCP JSON-RPC protocol envelope.
/// </summary>

/// <summary>
/// Describes a single tool available through the MCP protocol with its schema.
/// Maps directly to MCP tool definition specification.
/// </summary>
/// <param name="Name">Unique identifier for the tool (e.g., "get_customer_history")</param>
/// <param name="Description">Human-readable description of what the tool does</param>
/// <param name="InputSchema">JSON Schema defining the tool's required input parameters</param>
public record McpToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] McpInputSchema InputSchema
);

/// <summary>
/// Response wrapper for the gateway's REST tools listing endpoint.
/// </summary>
/// <param name="Tools">List of all available tool definitions</param>
public record McpListToolsResponse(
    [property: JsonPropertyName("tools")] List<McpToolDefinition> Tools
);

/// <summary>
/// JSON Schema container defining input parameters for a tool using JSON Schema Draft 7 standard.
/// </summary>
/// <param name="Type">Schema type, typically "object" for tool inputs (required by JSON Schema)</param>
/// <param name="Properties">Dictionary mapping parameter names to their type definitions</param>
/// <param name="Required">List of parameter names that must be provided by the LLM client</param>
public record McpInputSchema(
    [property: JsonPropertyName("type")] string Type = "object",
    [property: JsonPropertyName("properties")] Dictionary<string, McpPropertyDefinition>? Properties = null,
    [property: JsonPropertyName("required")] List<string>? Required = null
);

/// <summary>
/// Defines a single input parameter for a tool using JSON Schema type system.
/// </summary>
/// <param name="Type">JSON Schema type (e.g., "string", "integer", "boolean", "array")</param>
/// <param name="Description">Human-readable explanation of the parameter's purpose and constraints</param>
public record McpPropertyDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description
);

/// <summary>
/// Request contract for MCP tools/call endpoint.
/// Sent by the LLM client to execute a tool with specific arguments.
/// </summary>
/// <param name="Name">The tool to invoke (must match a defined McpToolDefinition.Name)</param>
/// <param name="Arguments">Dictionary of argument names to values, matching the tool's InputSchema</param>
public record McpCallToolRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] Dictionary<string, object> Arguments
);

/// <summary>
/// Response contract for MCP tools/call endpoint.
/// Returns tool execution results or error information back to the LLM client.
/// </summary>
/// <param name="Content">List of content blocks returned by the tool (typically text results)</param>
/// <param name="IsError">Flag indicating whether the tool execution encountered an error</param>
public record McpCallToolResponse(
    [property: JsonPropertyName("content")] List<McpContentText> Content,
    [property: JsonPropertyName("isError")] bool IsError = false
);

/// <summary>
/// Content block returned by tool execution, supporting multiple media types.
/// Minimal API responses typically use "text" type for string content.
/// </summary>
/// <param name="Type">MIME type of content: "text" for plain text, "image/*" for images, etc.</param>
/// <param name="Text">The actual content value (required for "text" type)</param>
public record McpContentText(
    [property: JsonPropertyName("type")] string Type = "text",
    [property: JsonPropertyName("text")] string Text = ""
);
