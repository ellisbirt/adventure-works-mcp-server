using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace EnterpriseAiGateway.Integration.Anthropic;

public static class AnthropicServiceCollectionExtensions
{
    public static IServiceCollection AddAnthropicClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var anthropicConfig = configuration.GetSection("Anthropic");

        var apiKey = anthropicConfig["ApiKey"] 
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required in configuration");

        var model = anthropicConfig["Model"] ?? "claude-3-5-sonnet-20241022";
        var maxTokens = int.TryParse(anthropicConfig["MaxTokens"], out var tokens) ? tokens : 1024;
        var timeoutSeconds = int.TryParse(anthropicConfig["RequestTimeoutSeconds"], out var seconds) ? seconds : 30;
        var retryAttempts = int.TryParse(anthropicConfig["RetryMaxAttempts"], out var retries) ? Math.Clamp(retries, 0, 5) : 3;
        var retryDelaySeconds = double.TryParse(anthropicConfig["RetryDelaySeconds"], out var delay) ? Math.Clamp(delay, 0.1, 10) : 1;
        var circuitBreakSeconds = int.TryParse(anthropicConfig["CircuitBreakDurationSeconds"], out var breakDuration) ? Math.Clamp(breakDuration, 5, 120) : 30;

        services.AddHttpClient("AnthropicClient", client =>
        {
            // Auth/beta headers are added per-request in AnthropicClient rather than here, since
            // this factory-managed client is reused for the app's lifetime.
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

        services.AddSingleton<IAnthropicClient>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
            return new AnthropicClient(factory, logger, apiKey, model, maxTokens, timeoutSeconds);
        });

        return services;
    }
}
