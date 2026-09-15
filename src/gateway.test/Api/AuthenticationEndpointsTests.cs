using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnterpriseAiGateway.Tests.Api;

public class AuthenticationEndpointsTests
{
    [Theory]
    [InlineData("/api/v1/mcp/tools")]
    [InlineData("/mcp/tools")]
    [InlineData("/api/v1/chat")]
    [InlineData("/chat")]
    public async Task ProtectedEndpoint_WithoutBearerToken_ReturnsUnauthorized(string path)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Enabled", "true");
                builder.UseSetting("Authentication:Authority", "https://login.microsoftonline.com/example-tenant/v2.0");
                builder.UseSetting("Authentication:Audience", "api://enterprise-ai-gateway");
            });
        using var client = factory.CreateClient();

        var response = path.EndsWith("chat", StringComparison.Ordinal)
            ? await client.PostAsync(path, new StringContent("{\"message\":\"hello\"}", System.Text.Encoding.UTF8, "application/json"))
            : await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}