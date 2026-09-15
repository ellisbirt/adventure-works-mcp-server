using Microsoft.Extensions.Logging;

namespace EnterpriseAiGateway.Logging;

internal static partial class AnthropicLogMessages
{
    [LoggerMessage(LogLevel.Trace, "Sending message to Anthropic API with system prompt caching enabled. Model: {Model}")]
    public static partial void SendingMessage(ILogger logger, string model);

    [LoggerMessage(LogLevel.Error, "Anthropic API error: {StatusCode}; response body length={ErrorBodyLength}")]
    public static partial void ApiError(ILogger logger, int statusCode, int errorBodyLength);

    [LoggerMessage(LogLevel.Information, "Anthropic API usage: InputTokens={InputTokens}, OutputTokens={OutputTokens}, CacheCreation={CacheCreation}, CacheRead={CacheRead}")]
    public static partial void Usage(ILogger logger, int inputTokens, int outputTokens, int cacheCreation, int cacheRead);

    [LoggerMessage(LogLevel.Information, "Prompt cache hit detected. Saved {CacheReadTokens} input tokens")]
    public static partial void CacheHit(ILogger logger, int cacheReadTokens);

    [LoggerMessage(LogLevel.Trace, "Successfully received response from Anthropic API. Tokens: {InputTokens}/{OutputTokens}")]
    public static partial void ResponseReceived(ILogger logger, int inputTokens, int outputTokens);

    [LoggerMessage(LogLevel.Error, "Anthropic API request timeout after {TimeoutSeconds}s")]
    public static partial void RequestTimeout(ILogger logger, double timeoutSeconds);

    [LoggerMessage(LogLevel.Error, "HTTP error communicating with Anthropic API")]
    public static partial void HttpError(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Unexpected error in AnthropicClient.SendMessageAsync")]
    public static partial void UnexpectedError(ILogger logger, Exception exception);
}