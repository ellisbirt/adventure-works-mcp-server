using System.Text.Json.Serialization;

namespace EnterpriseAiGateway.Integration.Anthropic;

/// <summary>
/// Anthropic API contract definitions for prompt caching integration.
/// Aligns with Claude 3 Message API specification with cache_control support.
/// </summary>

/// <summary>
/// Represents a single message content block with optional prompt caching metadata.
/// </summary>
public record AnthropicContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cache_control")] AnthropicCacheControl? CacheControl = null
);

/// <summary>
/// Prompt caching metadata following Anthropic Beta specification.
/// </summary>
public record AnthropicCacheControl(
    [property: JsonPropertyName("type")] string Type = "ephemeral"
);

/// <summary>
/// System prompt configuration with caching control for LLM context reuse.
/// </summary>
public record AnthropicSystemPrompt(
    [property: JsonPropertyName("type")] string Type = "text",
    [property: JsonPropertyName("text")] string Text = "",
    [property: JsonPropertyName("cache_control")] AnthropicCacheControl? CacheControl = null
);

/// <summary>
/// User message content for tool output forwarding.
/// </summary>
public record AnthropicUserMessage(
    [property: JsonPropertyName("role")] string Role = "user",
    [property: JsonPropertyName("content")] string Content = ""
);

/// <summary>
/// Request contract for Anthropic Messages API with prompt caching.
/// Maps directly to Claude 3 message creation endpoint.
/// </summary>
public record AnthropicMessageRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] object? System = null,
    [property: JsonPropertyName("messages")] List<AnthropicUserMessage>? Messages = null
);

/// <summary>
/// Response contract from Anthropic Messages API.
/// Contains generated text and usage metadata for cache hit detection.
/// </summary>
public record AnthropicMessageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] List<AnthropicContent> Content,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("usage")] AnthropicUsage Usage
);

/// <summary>
/// Content block in Anthropic response.
/// </summary>
public record AnthropicContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null
);

/// <summary>
/// Token usage metrics including prompt caching statistics.
/// cache_read_input_tokens indicates successful cache hit for system prompt.
/// </summary>
public record AnthropicUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens = null,
    [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens = null
);
