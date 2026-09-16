using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using EnterpriseAiGateway.Integration.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnterpriseAiGateway.Tests.Api;

public class ChatApiEndpointsTests
{
    [Fact]
    public async Task Chat_WithoutConfiguredAnthropicClient_ReturnsStructuredServiceUnavailableError()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Authentication:Enabled", "false"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "who is our best customer" });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().NotBeNull();
        var payload = body!;
        payload.Should().ContainKey("error");
        var errorMessage = payload["error"];
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("unavailable");
    }

    [Fact]
    public async Task Chat_WhenToolExecutionFails_ReturnsStructuredBadGatewayError()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Enabled", "false");
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(item => item.ServiceType == typeof(IMcpChatService));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddSingleton<IMcpChatService>(new ThrowingChatService());
                });
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "what changed?" });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        body.Should().NotBeNull();
        body!["error"].Should().Contain("could not complete");
    }

    private sealed class ThrowingChatService : IMcpChatService
    {
        public Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("tool failed");
    }
}
