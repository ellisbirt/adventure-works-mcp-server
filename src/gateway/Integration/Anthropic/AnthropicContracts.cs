using System.Text.Json.Serialization;

namespace EnterpriseAiGateway.Integration.Anthropic;

// Claude Messages API request/response shapes. CacheControl is only meaningful on the system
// prompt and content blocks per Anthropic's prompt-caching beta; AnthropicUserMessage has no
// cache_control field because per-request tool output is never worth caching.

public record AnthropicContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cache_control")] AnthropicCacheControl? CacheControl = null
);

public record AnthropicCacheControl(
    [property: JsonPropertyName("type")] string Type = "ephemeral"
);

public record AnthropicSystemPrompt(
    [property: JsonPropertyName("type")] string Type = "text",
    [property: JsonPropertyName("text")] string Text = "",
    [property: JsonPropertyName("cache_control")] AnthropicCacheControl? CacheControl = null
);

public record AnthropicUserMessage(
    [property: JsonPropertyName("role")] string Role = "user",
    [property: JsonPropertyName("content")] string Content = ""
);

public record AnthropicMessageRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] object? System = null,
    [property: JsonPropertyName("messages")] List<AnthropicUserMessage>? Messages = null
);

public record AnthropicMessageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] List<AnthropicContent> Content,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("usage")] AnthropicUsage Usage
);

public record AnthropicContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null
);

/// <summary>
/// CacheReadInputTokens > 0 means the system prompt was served from Anthropic's prompt cache
/// instead of being re-processed as full input tokens for this call.
/// </summary>
public record AnthropicUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens = null,
    [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens = null
);
