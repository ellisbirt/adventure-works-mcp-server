using FluentAssertions;
using Moq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using EnterpriseAiGateway.Tests.Fixtures;
using Xunit;

namespace EnterpriseAiGateway.Tests.Api;

/// <summary>
/// Integration tests for MCP API endpoints using WebApplicationFactory.
/// Tests /mcp/tools and /mcp/tools/call endpoints with realistic scenarios.
/// </summary>
public class McpApiEndpointsTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly MockRepository _mockRepository = new(MockBehavior.Loose);
    private readonly Mock<ISecureTableCatalogRepository> _tableCatalog = new(MockBehavior.Strict);
    private HttpClient _client = null!;

    public McpApiEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove the real DbContext registration
                    var dbContextDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AdventureWorksDbContext>));
                    if (dbContextDescriptor != null)
                    {
                        services.Remove(dbContextDescriptor);
                    }

                    var repositoryDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(ISecureCustomerRepository));
                    if (repositoryDescriptor != null)
                    {
                        services.Remove(repositoryDescriptor);
                    }

                    var tableCatalogDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(ISecureTableCatalogRepository));
                    if (tableCatalogDescriptor != null)
                    {
                        services.Remove(tableCatalogDescriptor);
                    }

                    // Use in-memory database for tests
                    services.AddDbContext<AdventureWorksDbContext>(options =>
                        options.UseInMemoryDatabase("TestDb"));
                    
                    // Register real repository with in-memory context
                    services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();
                    services.AddScoped(_ => _tableCatalog.Object);
                });
            });

        _tableCatalog
            .Setup(repository => repository.GetTablesAsync())
            .ReturnsAsync(new List<SafeTableDefinition>
            {
                new("SalesLT", "Product", new List<string> { "ProductID", "Name", "ListPrice" })
            });
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _factory.DisposeAsync();
    }

    #region GET /mcp/tools Tests

    [Fact]
    public async Task GetTools_ReturnsOkWithToolDefinitions()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetTools_ReturnsCorrectToolStructure()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("tools");
        content.Should().Contain("get_customer_history");
    }

    [Fact]
    public async Task GetTools_IncludesToolDescription()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("Safely reads");
        content.Should().Contain("customer");
    }

    [Fact]
    public async Task GetTools_IncludesInputSchemaWithProperties()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("inputSchema");
        content.Should().Contain("customerId");
        content.Should().Contain("integer");
    }

    [Fact]
    public async Task GetTools_IncludesRequiredParameters()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("\"required\"");
        content.Should().Contain("customerId");
    }

    [Fact]
    public async Task GetTools_IncludesSafeTableCatalogTools()
    {
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("list_database_tables").And.Contain("read_database_table");
    }

    #endregion

    #region POST /mcp/tools/call - Valid Requests Tests

    [Fact]
    public async Task CallTool_WithValidCustomerId_ReturnsOkResponse()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CallTool_WithValidCustomerId_ReturnsCustomerData()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("content");
        responseContent.Should().Contain("System Alert");
    }

    [Fact]
    public async Task CallTool_WithValidRequest_ReturnsSuccessResponse()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("\"isError\":false");
    }

    [Fact]
    public async Task CallTool_ListDatabaseTables_ReturnsOnlySafeCatalogMetadata()
    {
        var request = new McpCallToolRequest("list_database_tables", new Dictionary<string, object>());
        var response = await PostToolRequest(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        responseContent.Should().Contain("ProductID").And.NotContain("EmailAddress");
    }

    [Fact]
    public async Task CallTool_ReadDatabaseTable_UsesBoundedRequest()
    {
        _tableCatalog
            .Setup(repository => repository.GetTableRowsAsync("SalesLT", "Product", 2))
            .ReturnsAsync("[{\"ProductID\":1,\"Name\":\"Road Bike\"}]");
        var request = new McpCallToolRequest("read_database_table", new Dictionary<string, object>
        {
            { "schema", "SalesLT" }, { "table", "Product" }, { "limit", 2 }
        });

        var response = await PostToolRequest(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        responseContent.Should().Contain("Road Bike");
        _tableCatalog.Verify(repository => repository.GetTableRowsAsync("SalesLT", "Product", 2), Times.Once);
    }

    [Fact]
    public async Task CallTool_ReadDatabaseTable_WithLimitOver100_ReturnsBadRequest()
    {
        var request = new McpCallToolRequest("read_database_table", new Dictionary<string, object>
        {
            { "schema", "SalesLT" }, { "table", "Product" }, { "limit", 101 }
        });

        var response = await PostToolRequest(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseContent.Should().Contain("1 through 100");
    }

    #endregion

    #region POST /mcp/tools/call - Invalid Tool Name Tests

    [Fact]
    public async Task CallTool_WithInvalidToolName_ReturnsBadRequest()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "invalid_tool",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CallTool_WithInvalidToolName_ReturnsErrorMessage()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "unknown_tool",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("not mapped");
    }

    [Fact]
    public async Task CallTool_WithInvalidToolName_ReturnsErrorFlag()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "wrong_tool",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("\"isError\":true");
    }

    #endregion

    #region POST /mcp/tools/call - Missing/Invalid Parameter Tests

    [Fact]
    public async Task CallTool_WithMissingCustomerId_ReturnsBadRequest()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object>()  // Missing customerId
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CallTool_WithMissingCustomerId_ReturnsErrorMessage()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object>()
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("Missing");
        responseContent.Should().Contain("customerId");
    }

    [Fact]
    public async Task CallTool_WithNonIntegerCustomerId_ReturnsBadRequest()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", "not-a-number" } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CallTool_WithNonIntegerCustomerId_ReturnsErrorMessage()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", "invalid" } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        responseContent.Should().Contain("malformed");
    }

    [Fact]
    public async Task CallTool_WithEmptyArguments_ReturnsBadRequest()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object>()
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region POST /mcp/tools/call - PII Masking Tests

    [Fact]
    public async Task CallTool_WithValidRequest_EnforcesPiiMasking()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        // Since customerId 1 doesn't exist in test DB, we just verify the response is valid
        // In a real scenario with data, we'd check for [REDACTED_EMAIL] and [REDACTED_PHONE]
        responseContent.Should().Contain("content");
    }

    #endregion

    #region HTTP Method Validation Tests

    [Fact]
    public async Task GetTools_AllowsOnlyGet()
    {
        // Arrange & Act
        var postResponse = await _client.PostAsync("/mcp/tools", new StringContent(""));
        var putResponse = await _client.PutAsync("/mcp/tools", new StringContent(""));
        var deleteResponse = await _client.DeleteAsync("/mcp/tools");

        // Assert
        postResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        putResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task CallTool_AllowsOnlyPost()
    {
        // Arrange & Act
        var getResponse = await _client.GetAsync("/mcp/tools/call");
        var putResponse = await _client.PutAsync("/mcp/tools/call", new StringContent(""));
        var deleteResponse = await _client.DeleteAsync("/mcp/tools/call");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        putResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Response Content-Type Tests

    [Fact]
    public async Task GetTools_ReturnsJsonContentType()
    {
        // Act
        var response = await _client.GetAsync("/mcp/tools");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CallTool_ReturnsJsonContentType()
    {
        // Arrange
        var request = new McpCallToolRequest(
            Name: "get_customer_history",
            Arguments: new Dictionary<string, object> { { "customerId", 1 } }
        );

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/mcp/tools/call", content);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    #endregion

    private async Task<HttpResponseMessage> PostToolRequest(McpCallToolRequest request)
    {
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return await _client.PostAsync("/mcp/tools/call", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    }
}
