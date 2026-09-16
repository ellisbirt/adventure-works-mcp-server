using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Core.Mcp;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Integration.Anthropic;
using EnterpriseAiGateway.Integration.Chat;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace EnterpriseAiGateway.Tests.Integration.Chat;

public class McpChatServiceTests
{
    private static readonly IReadOnlyList<SafeTableDefinition> Catalog =
    [new("SalesLT", "Product", new[] { "ProductID", "Name", "ListPrice" })];

    private static bool IsReadRequest(IReadOnlyDictionary<string, object> arguments, string schema, string table, int limit) =>
        arguments.TryGetValue("schema", out var s) && s.Equals(schema) &&
        arguments.TryGetValue("table", out var t) && t.Equals(table) &&
        arguments.TryGetValue("limit", out var l) && l.Equals(limit);

    [Fact]
    public async Task AskAsync_WithTableRequest_ExecutesOnlyCatalogApprovedToolAndReturnsAnswer()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("list_database_tables", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: System.Text.Json.JsonSerializer.Serialize(Catalog))]));
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("read_database_table", It.Is<IReadOnlyDictionary<string, object>>(args => IsReadRequest(args, "SalesLT", "Product", 2)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: "[{\"ProductID\":1,\"Name\":\"Road Bike\"}]")]));
        anthropic.SetupSequence(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tool\":\"read_database_table\",\"schema\":\"SalesLT\",\"table\":\"Product\",\"limit\":2}")
            .ReturnsAsync("Road Bike is available in the product catalog.");
        var service = new McpChatService(anthropic.Object, toolExecutor.Object);

        var result = await service.AskAsync("Tell me about Road Bike");

        result.Tool.Should().Be("read_database_table");
        result.Message.Should().Contain("Road Bike");
        toolExecutor.Verify(executor => executor.ExecuteToolAsync("read_database_table", It.Is<IReadOnlyDictionary<string, object>>(args => IsReadRequest(args, "SalesLT", "Product", 2)), It.IsAny<CancellationToken>()), Times.Once);
        anthropic.Verify(client => client.SendMessageAsync(
            It.IsAny<string>(),
            It.Is<string>(input => input.Contains("MCP result: [{\"ProductID\":1,\"Name\":\"Road Bike\"}]")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskAsync_WithUnknownTable_RejectsTheModelSelection()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("list_database_tables", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: System.Text.Json.JsonSerializer.Serialize(Catalog))]));
        anthropic.Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tool\":\"read_database_table\",\"schema\":\"SalesLT\",\"table\":\"Address\",\"limit\":20}");
        var service = new McpChatService(anthropic.Object, toolExecutor.Object);

        var action = () => service.AskAsync("Show addresses");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the safe MCP catalog*");
        toolExecutor.Verify(executor => executor.ExecuteToolAsync("read_database_table", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_WithMarkdownFencedRoutingResponse_StillParsesTheToolSelection()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("list_database_tables", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: System.Text.Json.JsonSerializer.Serialize(Catalog))]));
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("read_database_table", It.Is<IReadOnlyDictionary<string, object>>(args => IsReadRequest(args, "SalesLT", "Product", 2)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: "[{\"ProductID\":1,\"Name\":\"Road Bike\"}]")]));
        anthropic.SetupSequence(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n{\"tool\":\"read_database_table\",\"schema\":\"SalesLT\",\"table\":\"Product\",\"limit\":2}\n```")
            .ReturnsAsync("Road Bike is available in the product catalog.");
        var service = new McpChatService(anthropic.Object, toolExecutor.Object);

        var result = await service.AskAsync("Tell me about Road Bike");

        result.Tool.Should().Be("read_database_table");
        toolExecutor.Verify(executor => executor.ExecuteToolAsync("read_database_table", It.Is<IReadOnlyDictionary<string, object>>(args => IsReadRequest(args, "SalesLT", "Product", 2)), It.IsAny<CancellationToken>()), Times.Once);
    }
}