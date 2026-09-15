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

public sealed class AnthropicProviderResponseException : HttpRequestException
{
    public AnthropicProviderResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Anthropic Messages API client. Tags the system prompt as an ephemeral prompt-cache entry so a
/// repeated system prompt (identical across calls) is billed and latency-costed as a cache hit
/// instead of full input tokens on every request.
/// </summary>
public interface IAnthropicClient
{
    Task<string> SendMessageAsync(string systemPrompt, string toolOutput, CancellationToken cancellationToken = default);
}

public class AnthropicClient : IAnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicClient> _logger;
    private readonly string _anthropicApiKey;
    private readonly string _anthropicModel;
    private readonly int _maxTokens;
    private readonly TimeSpan _requestTimeout;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicClient> logger,
        string apiKey,
        string model = "claude-sonnet-5",
        int maxTokens = 1024,
        int requestTimeoutSeconds = 30)
    {
        _httpClient = httpClientFactory.CreateClient("AnthropicClient");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _anthropicApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _anthropicModel = model ?? throw new ArgumentNullException(nameof(model));
        _maxTokens = maxTokens;
        _requestTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);

        ConfigureDefaultHeaders();
    }

    private void ConfigureDefaultHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _anthropicApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "EnterpriseAiGateway/1.0");
    }

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

            // Only the system prompt gets cache_control: it repeats verbatim across calls, while
            // toolOutput is unique per request and would never produce a cache hit anyway.
            var systemPromptObject = new AnthropicSystemPrompt(
                Text: systemPrompt,
                CacheControl: new AnthropicCacheControl(Type: "ephemeral")
            );

            var userMessage = new AnthropicUserMessage(
                Role: "user",
                Content: toolOutput
            );

            var request = new AnthropicMessageRequest(
                Model: _anthropicModel,
                MaxTokens: _maxTokens,
                // Anthropic requires "system" to be an array of content blocks (not a bare
                // object) whenever a block carries cache_control.
                System: new List<AnthropicSystemPrompt> { systemPromptObject },
                Messages: new List<AnthropicUserMessage> { userMessage }
            );

            AnthropicLogMessages.SendingMessage(_logger, _anthropicModel);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            if (!string.IsNullOrWhiteSpace(CorrelationContext.Id))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", CorrelationContext.Id);
            }

            var response = await _httpClient.SendAsync(httpRequest, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                AnthropicLogMessages.ApiError(_logger, (int)response.StatusCode, errorContent.Length);

                // 429/5xx are transient provider trouble callers can retry or circuit-break on;
                // other statuses are request/auth problems that retrying won't fix.
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    throw new AnthropicProviderUnavailableException("Anthropic API is temporarily unavailable.", statusCode: response.StatusCode);
                }

                throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}.", null, response.StatusCode);
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<AnthropicMessageResponse>(JsonOptions, cts.Token);

            if (apiResponse == null)
            {
                throw new InvalidOperationException("Anthropic API returned empty response body");
            }

            var responseText = ExtractResponseText(apiResponse);
            LogUsageMetrics(apiResponse.Usage);

            AnthropicLogMessages.ResponseReceived(
                _logger,
                apiResponse.Usage.InputTokens,
                apiResponse.Usage.OutputTokens);

            return responseText ?? string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
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
        catch (JsonException ex)
        {
            AnthropicLogMessages.InvalidResponse(_logger, ex);
            throw new AnthropicProviderResponseException("Anthropic API returned an invalid response.", ex);
        }
        catch (Exception ex)
        {
            AnthropicLogMessages.UnexpectedError(_logger, ex);
            throw;
        }
    }

    private static string ExtractResponseText(AnthropicMessageResponse response)
    {
        if (response?.Content == null || response.Content.Count == 0)
            return string.Empty;

        var textBlocks = response.Content
            .Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text))
            .Select(c => c.Text);

        return string.Join("\n", textBlocks);
    }

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
