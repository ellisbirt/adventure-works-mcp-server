using System.Net;
using System.Net.Http.Json;
using EnterpriseAiGateway.Integration.Chat;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task Chat_WithToolValidationError_ReturnsStructuredBadRequestError()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Enabled", "false");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMcpChatService>();
                    services.AddScoped<IMcpChatService, FailingMcpChatService>();
                });
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "top products" });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().NotBeNull();
        body!.Should().ContainKey("error");
        body["error"].Should().Be("Invalid startDate filter.");
    }
}

internal sealed class FailingMcpChatService : IMcpChatService
{
    public Task<McpChatResult> AskAsync(string message, CancellationToken cancellationToken = default) =>
        throw new McpChatToolException("Invalid startDate filter.");
}
