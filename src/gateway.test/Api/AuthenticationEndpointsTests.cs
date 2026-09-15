using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseAiGateway.Tests.Api;

public class AuthenticationEndpointsTests
{
    [Theory]
    [InlineData("/api/v1/mcp")]
    [InlineData("/api/v1/chat")]
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
            : await client.PostAsync(path, new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedMcpEndpoint_WithRequiredScope_IsAuthorized()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Enabled", "true");
                builder.UseSetting("Authentication:Authority", "https://login.microsoftonline.com/example-tenant/v2.0");
                builder.UseSetting("Authentication:Audience", "api://enterprise-ai-gateway");
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                });
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", "test-user"),
            new Claim("scp", "access_as_user")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}