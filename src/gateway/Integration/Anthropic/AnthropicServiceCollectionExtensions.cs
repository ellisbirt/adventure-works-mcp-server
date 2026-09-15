using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace EnterpriseAiGateway.Integration.Anthropic;

/// <summary>
/// Dependency injection extension methods for Anthropic integration services.
/// Enables clean service registration in Program.cs with configuration-driven setup.
/// </summary>
public static class AnthropicServiceCollectionExtensions
{
    /// <summary>
    /// Registers AnthropicClient with HttpClientFactory and configuration-based API credentials.
    /// 
    /// Configuration Requirements:
    /// Add to appsettings.json:
    /// {
    ///   "Anthropic": {
    ///     "ApiKey": "sk-ant-...",
    ///     "Model": "claude-3-5-sonnet-20241022",
    ///     "MaxTokens": 1024,
    ///     "RequestTimeoutSeconds": 30
    ///   }
    /// }
    /// 
    /// Usage in Program.cs:
    /// var configuration = builder.Configuration;
    /// builder.Services.AddAnthropicClient(configuration);
    /// </summary>
    public static IServiceCollection AddAnthropicClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Extract Anthropic configuration section
        var anthropicConfig = configuration.GetSection("Anthropic");

        var apiKey = anthropicConfig["ApiKey"] 
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required in configuration");

        var model = anthropicConfig["Model"] ?? "claude-3-5-sonnet-20241022";
        var maxTokens = int.TryParse(anthropicConfig["MaxTokens"], out var tokens) ? tokens : 1024;
        var timeoutSeconds = int.TryParse(anthropicConfig["RequestTimeoutSeconds"], out var seconds) ? seconds : 30;
        var retryAttempts = int.TryParse(anthropicConfig["RetryMaxAttempts"], out var retries) ? Math.Clamp(retries, 0, 5) : 3;
        var retryDelaySeconds = double.TryParse(anthropicConfig["RetryDelaySeconds"], out var delay) ? Math.Clamp(delay, 0.1, 10) : 1;
        var circuitBreakSeconds = int.TryParse(anthropicConfig["CircuitBreakDurationSeconds"], out var breakDuration) ? Math.Clamp(breakDuration, 5, 120) : 30;

        // Register named HttpClient for Anthropic API communication
        services.AddHttpClient("AnthropicClient", client =>
        {
            // HttpClient base configuration (headers added per-request in AnthropicClient)
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            options.Retry.MaxRetryAttempts = retryAttempts;
            options.Retry.Delay = TimeSpan.FromSeconds(retryDelaySeconds);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(circuitBreakSeconds);
        });

        // Register AnthropicClient as singleton (thread-safe, stateless service)
        services.AddSingleton<IAnthropicClient>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
            return new AnthropicClient(factory, logger, apiKey, model, maxTokens, timeoutSeconds);
        });

        return services;
    }
}
