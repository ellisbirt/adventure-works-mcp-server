using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using EnterpriseAiGateway.Logging;
using EnterpriseAiGateway.Infrastructure;
using Microsoft.Extensions.Logging;

namespace EnterpriseAiGateway.Integration.Anthropic;

public sealed class AnthropicProviderUnavailableException : HttpRequestException
{
    public AnthropicProviderUnavailableException(string message, Exception? innerException = null, HttpStatusCode? statusCode = null)
        : base(message, innerException, statusCode)
    {
    }
}

/// <summary>
/// Integration client for Anthropic Claude API with prompt caching support.
/// 
/// Design Principles:
/// - HttpClientFactory ensures connection pooling and proper socket reuse
/// - Prompt caching headers enable cost reduction and latency optimization
/// - System prompts are cached with ephemeral cache_control for context reuse
/// - Dependency injection enables testability and configuration externalization
/// 
/// Caching Strategy:
/// The system prompt is marked with cache_control: {"type": "ephemeral"} to enable
/// Anthropic's prompt caching feature, which reuses cached embeddings for repeated
/// system prompts across multiple requests. This reduces token consumption and API costs.
/// 
/// Beta API Headers:
/// All requests include anthropic-beta: "prompt-caching-2024-07-31" in extra_headers
/// to enable the prompt caching feature (currently in beta with Anthropic).
/// </summary>
public interface IAnthropicClient
{
    /// <summary>
    /// Sends a message to Claude API with system context and user tool outputs.
    /// </summary>
    /// <param name="systemPrompt">System prompt to establish context boundaries and instructions</param>
    /// <param name="toolOutput">User message containing tool execution results for LLM ingestion</param>
    /// <param name="cancellationToken">Cancellation token for async operation control</param>
    /// <returns>LLM response text from Claude</returns>
    Task<string> SendMessageAsync(string systemPrompt, string toolOutput, CancellationToken cancellationToken = default);
}

/// <summary>
/// Anthropic Claude integration service with enterprise-grade reliability.
/// 
/// Thread Safety:
/// - HttpClient obtained from factory is thread-safe for concurrent usage
/// - JsonSerializerOptions cached as static for allocation efficiency
/// - All async operations properly propagate CancellationToken for graceful shutdown
/// 
/// Error Handling:
/// - HttpRequestException with detailed logging on network failures
/// - Anthropic API errors parsed from response body for diagnostic insight
/// - Timeout exceptions explicitly handled with configurable duration
/// 
/// Observability:
/// - Request/response logging at Trace level for debugging
/// - Telemetry for cache hit detection via usage.cache_read_input_tokens
/// - Token usage metrics logged for cost allocation and quota monitoring
/// </summary>
public class AnthropicClient : IAnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicClient> _logger;
    private readonly string _anthropicApiKey;
    private readonly string _anthropicModel;
    private readonly int _maxTokens;
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    /// JSON serialization options for Anthropic API contracts.
    /// PropertyNamingPolicy.CamelCase aligns C# PascalCase properties to JSON camelCase.
    /// DefaultIgnoreCondition.WhenWritingNull prevents null serialization for optional fields.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes AnthropicClient with HttpClientFactory-managed client and configuration.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating and pooling HTTP connections</param>
    /// <param name="logger">Structured logger for observability</param>
    /// <param name="apiKey">Anthropic API key from configuration or secrets manager</param>
    /// <param name="model">Claude model identifier (e.g., "claude-3-5-sonnet-20241022")</param>
    /// <param name="maxTokens">Maximum tokens in LLM response (default 1024)</param>
    /// <param name="requestTimeoutSeconds">HTTP request timeout duration in seconds (default 30)</param>
    public AnthropicClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicClient> logger,
        string apiKey,
        string model = "claude-3-5-sonnet-20241022",
        int maxTokens = 1024,
        int requestTimeoutSeconds = 30)
    {
        _httpClient = httpClientFactory.CreateClient("AnthropicClient");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _anthropicApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _anthropicModel = model ?? throw new ArgumentNullException(nameof(model));
        _maxTokens = maxTokens;
        _requestTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);

        // Configure default headers for all requests
        ConfigureDefaultHeaders();
    }

    /// <summary>
    /// Configures HTTP default headers required for Anthropic API authentication and beta features.
    /// </summary>
    private void ConfigureDefaultHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _anthropicApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        
        // Enable prompt caching beta feature with explicit beta header
        _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");

        // Standard HTTP headers for API compatibility
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "EnterpriseAiGateway/1.0");
    }

    /// <summary>
    /// Sends a message to Anthropic Claude API with system prompt caching enabled.
    /// 
    /// Prompt Caching Implementation:
    /// - System prompt is wrapped in AnthropicSystemPrompt with cache_control: {"type": "ephemeral"}
    /// - This enables Anthropic's prompt caching to reuse cached embeddings on subsequent calls
    /// - Reduces token consumption for repeated system prompts (e.g., identical instructions)
    /// - Cache hits are detectable via response.usage.cache_read_input_tokens > 0
    /// 
    /// Request Structure:
    /// 1. Anthropic-Beta header is set to enable caching
    /// 2. System prompt object with cache_control metadata
    /// 3. User messages containing tool outputs for LLM processing
    /// 4. Max tokens and model configuration
    /// </summary>
    public async Task<string> SendMessageAsync(string systemPrompt, string toolOutput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentNullException(nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(toolOutput))
            throw new ArgumentNullException(nameof(toolOutput));

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_requestTimeout);

            // Construct system prompt with caching metadata for reuse
            var systemPromptObject = new AnthropicSystemPrompt(
                Text: systemPrompt,
                CacheControl: new AnthropicCacheControl(Type: "ephemeral")
            );

            // Construct user message with tool output context
            var userMessage = new AnthropicUserMessage(
                Role: "user",
                Content: toolOutput
            );

            // Build request with prompt caching enabled
            var request = new AnthropicMessageRequest(
                Model: _anthropicModel,
                MaxTokens: _maxTokens,
                System: systemPromptObject,
                Messages: new List<AnthropicUserMessage> { userMessage }
            );

            AnthropicLogMessages.SendingMessage(_logger, _anthropicModel);

            // POST to Anthropic Messages API endpoint
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            if (!string.IsNullOrWhiteSpace(CorrelationContext.Id))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", CorrelationContext.Id);
            }

            var response = await _httpClient.SendAsync(httpRequest, cts.Token);

            // Ensure successful response
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                AnthropicLogMessages.ApiError(_logger, (int)response.StatusCode, errorContent.Length);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    throw new AnthropicProviderUnavailableException("Anthropic API is temporarily unavailable.", statusCode: response.StatusCode);
                }

                throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}.", null, response.StatusCode);
            }

            // Deserialize response with usage metrics (including cache hit detection)
            var apiResponse = await response.Content.ReadFromJsonAsync<AnthropicMessageResponse>(JsonOptions, cts.Token);
            
            if (apiResponse == null)
            {
                throw new InvalidOperationException("Anthropic API returned empty response body");
            }

            // Extract text from response content blocks
            var responseText = ExtractResponseText(apiResponse);

            // Log token usage and cache hit metrics for observability
            LogUsageMetrics(apiResponse.Usage);

            AnthropicLogMessages.ResponseReceived(
                _logger,
                apiResponse.Usage.InputTokens,
                apiResponse.Usage.OutputTokens);

            return responseText ?? string.Empty;
        }
        catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException || _requestTimeout != Timeout.InfiniteTimeSpan)
        {
            AnthropicLogMessages.RequestTimeout(_logger, _requestTimeout.TotalSeconds);
            throw new AnthropicProviderUnavailableException($"Anthropic API request timed out after {_requestTimeout.TotalSeconds}s", ex);
        }
        catch (AnthropicProviderUnavailableException ex)
        {
            AnthropicLogMessages.HttpError(_logger, ex);
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            AnthropicLogMessages.HttpError(_logger, ex);
            throw new AnthropicProviderUnavailableException("Anthropic API network request failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            AnthropicLogMessages.HttpError(_logger, ex);
            throw;
        }
        catch (Exception ex)
        {
            AnthropicLogMessages.UnexpectedError(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// Extracts text content from Anthropic API response content blocks.
    /// </summary>
    private static string ExtractResponseText(AnthropicMessageResponse response)
    {
        if (response?.Content == null || response.Content.Count == 0)
            return string.Empty;

        // Concatenate text from all content blocks (typically only one for text responses)
        var textBlocks = response.Content
            .Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text))
            .Select(c => c.Text);

        return string.Join("\n", textBlocks);
    }

    /// <summary>
    /// Logs token usage metrics including prompt caching statistics.
    /// cache_read_input_tokens > 0 indicates successful cache hit on system prompt.
    /// </summary>
    private void LogUsageMetrics(AnthropicUsage usage)
    {
        AnthropicLogMessages.Usage(
            _logger,
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheCreationInputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0);

        if (usage.CacheReadInputTokens > 0)
        {
            AnthropicLogMessages.CacheHit(_logger, usage.CacheReadInputTokens.Value);
        }
    }
}
