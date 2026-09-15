using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EnterpriseAiGateway.Tests.Api;

public class McpApiEndpointsTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ISecureTableCatalogRepository> _tableCatalog = new(MockBehavior.Strict);
    private HttpClient _client = null!;

    public McpApiEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<AdventureWorksDbContext>));
            if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);
            var repositoryDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecureCustomerRepository));
            if (repositoryDescriptor is not null) services.Remove(repositoryDescriptor);
            var tableCatalogDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecureTableCatalogRepository));
            if (tableCatalogDescriptor is not null) services.Remove(tableCatalogDescriptor);

            services.AddDbContext<AdventureWorksDbContext>(options => options.UseInMemoryDatabase("McpJsonRpcTestDb"));
            services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();
            services.AddScoped(_ => _tableCatalog.Object);
        }));

        _tableCatalog.Setup(repository => repository.GetTablesAsync())
            .ReturnsAsync([new SafeTableDefinition("SalesLT", "Product", ["ProductID", "Name", "ListPrice"])]);
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Initialize_ReturnsNegotiatedJsonRpcResponse()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        body.GetProperty("id").GetInt32().Should().Be(1);
        body.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
        body.GetProperty("result").GetProperty("capabilities").GetProperty("tools").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ToolsList_ReturnsGovernedTools()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", id = "tools", method = "tools/list", @params = new { } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("id").GetString().Should().Be("tools");
        body.GetProperty("result").GetProperty("tools").GetArrayLength().Should().Be(3);
        body.GetProperty("result").GetProperty("tools")[0].GetProperty("inputSchema").GetProperty("required").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task CallTool_ReturnsMcpContentResult()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "list_database_tables", arguments = new { } } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("result").GetProperty("content")[0].GetProperty("type").GetString().Should().Be("text");
        body.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CallTool_ReturnsToolErrorResultForInvalidArguments()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "get_customer_history", arguments = new { } } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("customerId");
    }

    [Fact]
    public async Task UnknownMethod_ReturnsJsonRpcError()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", id = 4, method = "unknown/method", @params = new { } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("id").GetInt32().Should().Be(4);
        body.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    [Fact]
    public async Task InitializedNotification_ReturnsNoContent()
    {
        var response = await PostRpcAsync(new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_ReturnsResponsesAndOmitsNotifications()
    {
        var response = await PostRpcAsync(new object[]
        {
            new { jsonrpc = "2.0", id = 5, method = "tools/list", @params = new { } },
            new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } },
            new { jsonrpc = "2.0", id = 6, method = "unknown/method", @params = new { } }
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetArrayLength().Should().Be(2);
        body.EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).Should().Contain([5, 6]);
    }

    [Fact]
    public async Task InvalidJson_ReturnsParseError()
    {
        var response = await _client.PostAsync("/api/v1/mcp", new StringContent("{"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32700);
    }

    [Fact]
    public async Task Batch_RejectsMoreThanTwentyRequests()
    {
        var requests = Enumerable.Range(1, 21)
            .Select(id => (object)new { jsonrpc = "2.0", id, method = "tools/list", @params = new { } })
            .ToArray();
        var response = await PostRpcAsync(requests);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600);
    }

    [Fact]
    public async Task LegacyRestRoutes_AreRemoved()
    {
        (await _client.GetAsync("/api/v1/mcp/tools")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsJsonAsync("/api/v1/mcp/tools/call", new { name = "list_database_tables", arguments = new { } })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> PostRpcAsync(object request) =>
        await _client.PostAsJsonAsync("/api/v1/mcp", request);
}
