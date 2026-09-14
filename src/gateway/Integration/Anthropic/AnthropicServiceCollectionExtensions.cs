using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

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

        // Register named HttpClient for Anthropic API communication
        services.AddHttpClient("AnthropicClient", client =>
        {
            // HttpClient base configuration (headers added per-request in AnthropicClient)
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        // Add resilience policies (retry, circuit breaker) if needed
        // .AddTransientHttpErrorPolicy()...
        ;

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
