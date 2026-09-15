using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
        body!.Should().ContainKey("error");
        var errorMessage = body["error"];
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("unavailable");
    }
}
