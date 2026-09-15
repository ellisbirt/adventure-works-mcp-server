using System.Text.Json.Serialization;

namespace EnterpriseAiGateway.Core.DTOs;

public record ChatRequest(
    [property: JsonPropertyName("message")] string Message
);

public record ChatResponse(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("tool")] string Tool
);