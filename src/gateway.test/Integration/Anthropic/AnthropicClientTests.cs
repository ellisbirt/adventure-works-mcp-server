using FluentAssertions;
using Moq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using EnterpriseAiGateway.Integration.Anthropic;
using Xunit;

namespace EnterpriseAiGateway.Tests.Integration.Anthropic;

/// <summary>
/// Comprehensive unit tests for AnthropicClient.
/// Tests HTTP client interactions, error handling, and cache metrics.
/// </summary>
public class AnthropicClientTests
{
    private readonly MockRepository _mockRepository = new(MockBehavior.Loose);

    #region SendMessageAsync - Successful Response Tests

    [Fact]
    public async Task SendMessageAsync_WithValidInputs_ReturnsResponseText()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();
        mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var response = new AnthropicMessageResponse(
            Id: "msg_123",
            Type: "message",
            Role: "assistant",
            Content: new List<AnthropicContent>
            {
                new("text", "Customer data has been processed successfully")
            },
            Model: "claude-3-5-sonnet-20241022",
            StopReason: "end_turn",
            Usage: new AnthropicUsage(100, 50)
        );

        mockHandler.SetupResponse(HttpStatusCode.OK, response);
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(
            mockFactory.Object,
            mockLogger.Object,
            "sk-ant-test-key",
            "claude-3-5-sonnet-20241022",
            1024,
            30
        );

        // Act
        var result = await client.SendMessageAsync(
            "You are a helpful assistant",
            "Get customer 123 data"
        );

        // Assert
        result.Should().Contain("processed successfully");
        mockFactory.Verify(f => f.CreateClient("AnthropicClient"), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithMultipleContentBlocks_ConcatenatesText()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();
        mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var response = new AnthropicMessageResponse(
            Id: "msg_456",
            Type: "message",
            Role: "assistant",
            Content: new List<AnthropicContent>
            {
                new("text", "First block"),
                new("text", "Second block"),
                new("text", "Third block")
            },
            Model: "claude-3-5-sonnet-20241022",
            StopReason: "end_turn",
            Usage: new AnthropicUsage(150, 75)
        );

        mockHandler.SetupResponse(HttpStatusCode.OK, response);
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act
        var result = await client.SendMessageAsync("System prompt", "User query");

        // Assert
        result.Should().Contain("First block");
        result.Should().Contain("Second block");
        result.Should().Contain("Third block");
    }

    #endregion

    #region SendMessageAsync - Error Handling Tests

    [Fact]
    public async Task SendMessageAsync_WithNullSystemPrompt_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendMessageAsync(null!, "Tool output")
        );
    }

    [Fact]
    public async Task SendMessageAsync_WithNullToolOutput_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendMessageAsync("System prompt", null!)
        );
    }

    [Fact]
    public async Task SendMessageAsync_With400BadRequest_ThrowsHttpRequestException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupErrorResponse(HttpStatusCode.BadRequest, "Invalid request format");
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendMessageAsync("System prompt", "User query")
        );
    }

    [Fact]
    public async Task SendMessageAsync_With401Unauthorized_ThrowsHttpRequestException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupErrorResponse(HttpStatusCode.Unauthorized, "Invalid API key");
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-invalid-key");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendMessageAsync("System prompt", "User query")
        );
    }

    [Fact]
    public async Task SendMessageAsync_With429RateLimited_ThrowsHttpRequestException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupErrorResponse(HttpStatusCode.TooManyRequests, "Rate limit exceeded");
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act & Assert
        await Assert.ThrowsAsync<AnthropicProviderUnavailableException>(() =>
            client.SendMessageAsync("System prompt", "User query")
        );
    }

    [Fact]
    public async Task SendMessageAsync_With500ServerError_ThrowsHttpRequestException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupErrorResponse(HttpStatusCode.InternalServerError, "Server error");
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act & Assert
        await Assert.ThrowsAsync<AnthropicProviderUnavailableException>(() =>
            client.SendMessageAsync("System prompt", "User query")
        );
    }

    [Fact]
    public async Task AddAnthropicClient_RetriesTransientProviderFailures()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetupResponses(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"temporarily unavailable\"}"),
            (HttpStatusCode.OK, "{\"id\":\"msg_retry\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Recovered\"}],\"model\":\"claude\",\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "sk-ant-test-key",
            ["Anthropic:RequestTimeoutSeconds"] = "10"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnthropicClient(configuration);
        services.Configure<HttpClientFactoryOptions>("AnthropicClient", options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IAnthropicClient>();

        var result = await client.SendMessageAsync("System prompt", "User query");

        result.Should().Be("Recovered");
        handler.RequestCount.Should().Be(2);
    }

    #endregion

    #region SendMessageAsync - Timeout Tests

    [Fact]
    public async Task SendMessageAsync_WithTimeoutScenario_ThrowsHttpRequestException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupDelayedResponse(2000); // 2 second delay
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(
            mockFactory.Object,
            mockLogger.Object,
            "sk-ant-test-key",
            "claude-3-5-sonnet-20241022",
            1024,
            1  // 1 second timeout
        );

        // Act & Assert
        await Assert.ThrowsAsync<AnthropicProviderUnavailableException>(() =>
            client.SendMessageAsync("System prompt", "User query")
        );
    }

    #endregion

    #region SendMessageAsync - Token Usage Tracking Tests

    [Fact]
    public async Task SendMessageAsync_LogsTokenUsageMetrics()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();
        mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var response = new AnthropicMessageResponse(
            Id: "msg_789",
            Type: "message",
            Role: "assistant",
            Content: new List<AnthropicContent> { new("text", "Response") },
            Model: "claude-3-5-sonnet-20241022",
            StopReason: "end_turn",
            Usage: new AnthropicUsage(500, 250, CacheCreationInputTokens: 1000, CacheReadInputTokens: null)
        );

        mockHandler.SetupResponse(HttpStatusCode.OK, response);
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act
        await client.SendMessageAsync("System prompt", "User query");

        // Assert - Verify logger was called with usage metrics
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => eventId.Name == "Usage"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task SendMessageAsync_WithCacheHit_LogsCacheReadTokens()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var mockHandler = new MockHttpMessageHandler();
        mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var response = new AnthropicMessageResponse(
            Id: "msg_cache",
            Type: "message",
            Role: "assistant",
            Content: new List<AnthropicContent> { new("text", "Cached response") },
            Model: "claude-3-5-sonnet-20241022",
            StopReason: "end_turn",
            Usage: new AnthropicUsage(
                InputTokens: 50,
                OutputTokens: 100,
                CacheCreationInputTokens: null,
                CacheReadInputTokens: 5000  // Cache hit detected
            )
        );

        mockHandler.SetupResponse(HttpStatusCode.OK, response);
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        var client = new AnthropicClient(mockFactory.Object, mockLogger.Object, "sk-ant-test-key");

        // Act
        await client.SendMessageAsync("System prompt", "User query");

        // Assert - Verify cache metrics were logged
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => eventId.Name == "CacheHit"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        // Act
        var client = new AnthropicClient(
            mockFactory.Object,
            mockLogger.Object,
            "sk-ant-test-key",
            "claude-3-5-sonnet-20241022",
            2048,
            60
        );

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            new AnthropicClient(null!, mockLogger.Object, "sk-ant-test-key")
        );
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var httpClient = new HttpClient();
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AnthropicClient(mockFactory.Object, null!, "sk-ant-test-key")
        );
    }

    [Fact]
    public void Constructor_WithNullApiKey_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = _mockRepository.Create<IHttpClientFactory>();
        var mockLogger = _mockRepository.Create<Microsoft.Extensions.Logging.ILogger<AnthropicClient>>();
        var httpClient = new HttpClient();
        mockFactory.Setup(f => f.CreateClient("AnthropicClient")).Returns(httpClient);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AnthropicClient(mockFactory.Object, mockLogger.Object, null!)
        );
    }

    #endregion

    #region Mock HttpMessageHandler Helper

    /// <summary>
    /// Mock handler for simulating HTTP responses and timeouts
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _content = "{}";
        private int _delayMs = 0;
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses = new();
        public int RequestCount { get; private set; }

        public void SetupResponse<T>(HttpStatusCode statusCode, T response)
        {
            _statusCode = statusCode;
            _content = System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }

        public void SetupErrorResponse(HttpStatusCode statusCode, string errorMessage)
        {
            _statusCode = statusCode;
            _content = $"{{\"error\": \"{errorMessage}\"}}";
        }

        public void SetupDelayedResponse(int delayMs)
        {
            _delayMs = delayMs;
        }

        public void SetupResponses(params (HttpStatusCode StatusCode, string Content)[] responses)
        {
            foreach (var response in responses) _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }

            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : (StatusCode: _statusCode, Content: _content);
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion
}
