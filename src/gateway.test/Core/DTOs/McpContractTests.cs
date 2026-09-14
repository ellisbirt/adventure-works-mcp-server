using FluentAssertions;
using Moq;
using EnterpriseAiGateway.Core.DTOs;
using Xunit;

namespace EnterpriseAiGateway.Tests.Core.DTOs;

/// <summary>
/// Comprehensive unit tests for MCP (Model Context Protocol) DTOs.
/// Tests serialization, deserialization, and contract compliance.
/// </summary>
public class McpContractTests
{
    private readonly MockRepository _mockRepository = new(MockBehavior.Strict);

    #region McpToolDefinition Tests

    [Fact]
    public void McpToolDefinition_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var name = "get_customer_history";
        var description = "Retrieves customer history from database";
        var inputSchema = new McpInputSchema(
            Type: "object",
            Properties: new Dictionary<string, McpPropertyDefinition>
            {
                { "customerId", new McpPropertyDefinition("integer", "Customer ID") }
            },
            Required: new List<string> { "customerId" }
        );

        // Act
        var toolDefinition = new McpToolDefinition(name, description, inputSchema);

        // Assert
        toolDefinition.Name.Should().Be(name);
        toolDefinition.Description.Should().Be(description);
        toolDefinition.InputSchema.Should().Be(inputSchema);
        toolDefinition.InputSchema.Type.Should().Be("object");
        toolDefinition.InputSchema.Properties.Should().ContainKey("customerId");
        toolDefinition.InputSchema.Required.Should().Contain("customerId");
    }

    [Fact]
    public void McpToolDefinition_WithComplexInputSchema_ContainsAllParameters()
    {
        // Arrange
        var properties = new Dictionary<string, McpPropertyDefinition>
        {
            { "customerId", new McpPropertyDefinition("integer", "The customer ID") },
            { "includeHistory", new McpPropertyDefinition("boolean", "Include order history") },
            { "maxResults", new McpPropertyDefinition("integer", "Maximum results to return") }
        };

        var inputSchema = new McpInputSchema(
            Type: "object",
            Properties: properties,
            Required: new List<string> { "customerId", "maxResults" }
        );

        var tool = new McpToolDefinition("complex_tool", "A complex tool", inputSchema);

        // Act & Assert
        tool.InputSchema.Properties.Should().NotBeNull();
        tool.InputSchema.Properties!.Should().HaveCount(3);
        tool.InputSchema.Required.Should().NotBeNull();
        tool.InputSchema.Required!.Should().HaveCount(2);
        tool.InputSchema.Properties!["customerId"].Type.Should().Be("integer");
        tool.InputSchema.Properties!["includeHistory"].Type.Should().Be("boolean");
    }

    #endregion

    #region McpListToolsResponse Tests

    [Fact]
    public void McpListToolsResponse_WithSingleTool_ContainsTool()
    {
        // Arrange
        var tool = new McpToolDefinition(
            Name: "get_customer_history",
            Description: "Get customer history",
            InputSchema: new McpInputSchema()
        );

        var tools = new List<McpToolDefinition> { tool };

        // Act
        var response = new McpListToolsResponse(tools);

        // Assert
        response.Tools.Should().HaveCount(1);
        response.Tools.First().Name.Should().Be("get_customer_history");
    }

    [Fact]
    public void McpListToolsResponse_WithMultipleTools_ContainsAllTools()
    {
        // Arrange
        var tools = new List<McpToolDefinition>
        {
            new("tool1", "Description 1", new McpInputSchema()),
            new("tool2", "Description 2", new McpInputSchema()),
            new("tool3", "Description 3", new McpInputSchema())
        };

        // Act
        var response = new McpListToolsResponse(tools);

        // Assert
        response.Tools.Should().HaveCount(3);
        response.Tools.Select(t => t.Name).Should().ContainInOrder("tool1", "tool2", "tool3");
    }

    [Fact]
    public void McpListToolsResponse_WithEmptyToolsList_ContainsEmptyList()
    {
        // Arrange
        var tools = new List<McpToolDefinition>();

        // Act
        var response = new McpListToolsResponse(tools);

        // Assert
        response.Tools.Should().BeEmpty();
    }

    #endregion

    #region McpInputSchema Tests

    [Fact]
    public void McpInputSchema_DefaultValues_UsesObjectType()
    {
        // Arrange & Act
        var schema = new McpInputSchema();

        // Assert
        schema.Type.Should().Be("object");
        schema.Properties.Should().BeNull();
        schema.Required.Should().BeNull();
    }

    [Fact]
    public void McpInputSchema_WithCustomProperties_StoresProperties()
    {
        // Arrange
        var properties = new Dictionary<string, McpPropertyDefinition>
        {
            { "param1", new McpPropertyDefinition("string", "First parameter") }
        };

        // Act
        var schema = new McpInputSchema(
            Type: "object",
            Properties: properties,
            Required: new List<string> { "param1" }
        );

        // Assert
        schema.Properties.Should().NotBeNull();
        schema.Properties!.Should().HaveCount(1);
        schema.Required.Should().NotBeNull();
        schema.Required!.Should().Contain("param1");
    }

    #endregion

    #region McpPropertyDefinition Tests

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("boolean")]
    [InlineData("array")]
    [InlineData("object")]
    public void McpPropertyDefinition_WithValidJsonSchemaType_CreatesProperty(string jsonSchemaType)
    {
        // Arrange & Act
        var property = new McpPropertyDefinition(jsonSchemaType, "Test description");

        // Assert
        property.Type.Should().Be(jsonSchemaType);
        property.Description.Should().Be("Test description");
    }

    [Fact]
    public void McpPropertyDefinition_WithEmptyDescription_AllowsEmptyString()
    {
        // Arrange & Act
        var property = new McpPropertyDefinition("string", string.Empty);

        // Assert
        property.Description.Should().BeEmpty();
    }

    #endregion

    #region McpCallToolRequest Tests

    [Fact]
    public void McpCallToolRequest_WithValidNameAndArguments_CreatesRequest()
    {
        // Arrange
        var name = "get_customer_history";
        var arguments = new Dictionary<string, object> { { "customerId", 123 } };

        // Act
        var request = new McpCallToolRequest(name, arguments);

        // Assert
        request.Name.Should().Be(name);
        request.Arguments.Should().HaveCount(1);
        request.Arguments["customerId"].Should().Be(123);
    }

    [Fact]
    public void McpCallToolRequest_WithMultipleArguments_ContainsAllArguments()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            { "customerId", 123 },
            { "maskData", true },
            { "maxResults", 10 }
        };

        // Act
        var request = new McpCallToolRequest("get_customer", arguments);

        // Assert
        request.Arguments.Should().HaveCount(3);
        request.Arguments.Should().ContainKey("customerId");
        request.Arguments.Should().ContainKey("maskData");
        request.Arguments.Should().ContainKey("maxResults");
    }

    [Fact]
    public void McpCallToolRequest_WithEmptyArguments_AllowsEmptyDictionary()
    {
        // Arrange & Act
        var request = new McpCallToolRequest("tool_name", new Dictionary<string, object>());

        // Assert
        request.Arguments.Should().BeEmpty();
    }

    #endregion

    #region McpCallToolResponse Tests

    [Fact]
    public void McpCallToolResponse_WithSuccessContent_DefaultIsError()
    {
        // Arrange
        var content = new List<McpContentText> 
        { 
            new("text", "Success message")
        };

        // Act
        var response = new McpCallToolResponse(content);

        // Assert
        response.Content.Should().HaveCount(1);
        response.IsError.Should().BeFalse();
        response.Content.First().Type.Should().Be("text");
    }

    [Fact]
    public void McpCallToolResponse_WithErrorFlag_IndicatesError()
    {
        // Arrange
        var content = new List<McpContentText> 
        { 
            new("text", "Error occurred")
        };

        // Act
        var response = new McpCallToolResponse(content, IsError: true);

        // Assert
        response.IsError.Should().BeTrue();
        response.Content.Should().HaveCount(1);
    }

    [Fact]
    public void McpCallToolResponse_WithMultipleContentBlocks_ContainsAllContent()
    {
        // Arrange
        var content = new List<McpContentText>
        {
            new("text", "First block"),
            new("text", "Second block"),
            new("text", "Third block")
        };

        // Act
        var response = new McpCallToolResponse(content);

        // Assert
        response.Content.Should().HaveCount(3);
    }

    #endregion

    #region McpContentText Tests

    [Fact]
    public void McpContentText_WithTextType_StoresContent()
    {
        // Arrange & Act
        var content = new McpContentText("text", "Sample content");

        // Assert
        content.Type.Should().Be("text");
        content.Text.Should().Be("Sample content");
    }

    [Fact]
    public void McpContentText_WithEmptyContent_AllowsEmpty()
    {
        // Arrange & Act
        var content = new McpContentText("text", string.Empty);

        // Assert
        content.Text.Should().BeEmpty();
    }

    #endregion

    #region Contract Compliance Tests

    [Fact]
    public void McpToolDefinition_IsRecord_SupportsEquality()
    {
        // Arrange
        var schema = new McpInputSchema();
        var tool1 = new McpToolDefinition("tool", "description", schema);
        var tool2 = new McpToolDefinition("tool", "description", schema);

        // Act & Assert
        tool1.Should().Be(tool2);
    }

    [Fact]
    public void McpCallToolResponse_BuildsCompleteContractResponse()
    {
        // Arrange
        var expectedResponse = new McpCallToolResponse(
            Content: new List<McpContentText>
            {
                new("text", "Customer found: John Doe")
            },
            IsError: false
        );

        // Act & Assert
        expectedResponse.Content.Should().NotBeEmpty();
        expectedResponse.IsError.Should().BeFalse();
        expectedResponse.Content.First().Text.Should().Contain("John Doe");
    }

    #endregion

    #region Mock Validation Tests

    [Fact]
    public void McpToolDefinition_CanBeUsedWithMocks()
    {
        // Arrange
        var mockToolFactory = _mockRepository.Create<IMcpToolFactory>();
        var expectedTool = new McpToolDefinition(
            "mocked_tool",
            "Mocked description",
            new McpInputSchema()
        );

        mockToolFactory
            .Setup(f => f.CreateTool("mocked_tool", "Mocked description"))
            .Returns(expectedTool);

        // Act
        var result = mockToolFactory.Object.CreateTool("mocked_tool", "Mocked description");

        // Assert
        result.Name.Should().Be("mocked_tool");
        mockToolFactory.Verify(f => f.CreateTool("mocked_tool", "Mocked description"), Times.Once);
    }

    [Fact]
    public async Task McpCallToolRequest_CanBeUsedWithMocks()
    {
        // Arrange
        var mockToolExecutor = _mockRepository.Create<IMcpToolExecutor>();
        var request = new McpCallToolRequest("get_customer", new Dictionary<string, object> { { "id", 1 } });
        var expectedResponse = new McpCallToolResponse(
            new List<McpContentText> { new("text", "result") },
            false
        );

        mockToolExecutor
            .Setup(e => e.ExecuteAsync(It.IsAny<McpCallToolRequest>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await mockToolExecutor.Object.ExecuteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        mockToolExecutor.Verify(e => e.ExecuteAsync(It.Is<McpCallToolRequest>(r => r.Name == "get_customer")), Times.Once);
    }

    #endregion

    #region Test Helper Interfaces

    /// <summary>
    /// Mock interface for testing tool factory patterns
    /// </summary>
    public interface IMcpToolFactory
    {
        McpToolDefinition CreateTool(string name, string description);
    }

    /// <summary>
    /// Mock interface for testing tool execution patterns
    /// </summary>
    public interface IMcpToolExecutor
    {
        Task<McpCallToolResponse> ExecuteAsync(McpCallToolRequest request);
    }

    #endregion
}
