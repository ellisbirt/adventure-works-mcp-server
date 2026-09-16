using System.Text.Json;
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
    private static readonly IReadOnlyList<McpToolDefinition> ToolDefinitions =
    [
        new("list_database_tables", "list", new McpInputSchema()),
        new("read_database_table", "read", new McpInputSchema()),
        new("get_top_selling_products_summary", "summary", new McpInputSchema()),
        new("get_highest_revenue_products_summary", "summary", new McpInputSchema())
    ];

    private static bool IsReadRequest(IReadOnlyDictionary<string, object> arguments, string schema, string table, int limit) =>
        arguments.TryGetValue("schema", out var s) && s.Equals(schema) &&
        arguments.TryGetValue("table", out var t) && t.Equals(table) &&
        arguments.TryGetValue("limit", out var l) && l.Equals(limit);

    private static bool IsSummaryRequest(IReadOnlyDictionary<string, object> arguments, int top, string startDate, string endDate) =>
        arguments.TryGetValue("top", out var topValue) &&
        topValue is JsonElement topElement &&
        topElement.ValueKind == JsonValueKind.Number &&
        topElement.GetInt32() == top &&
        arguments.TryGetValue("startDate", out var startDateValue) &&
        startDateValue is JsonElement startDateElement &&
        startDateElement.ValueKind == JsonValueKind.String &&
        startDateElement.GetString() == startDate &&
        arguments.TryGetValue("endDate", out var endDateValue) &&
        endDateValue is JsonElement endDateElement &&
        endDateElement.ValueKind == JsonValueKind.String &&
        endDateElement.GetString() == endDate;

    [Fact]
    public async Task AskAsync_WithTableRequest_ExecutesOnlyCatalogApprovedToolAndReturnsAnswer()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.GetToolDefinitions()).Returns(ToolDefinitions);
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
        toolExecutor.Setup(executor => executor.GetToolDefinitions()).Returns(ToolDefinitions);
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
        toolExecutor.Setup(executor => executor.GetToolDefinitions()).Returns(ToolDefinitions);
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

    [Fact]
    public async Task AskAsync_WithSalesSummaryRequest_ExecutesSummaryToolAndReturnsAnswer()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.GetToolDefinitions()).Returns(ToolDefinitions);
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("list_database_tables", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: System.Text.Json.JsonSerializer.Serialize(Catalog))]));
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("get_top_selling_products_summary", It.Is<IReadOnlyDictionary<string, object>>(args => IsSummaryRequest(args, 5, "2024-01-01", "2024-12-31")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: """{"metric":"top_selling_products_by_quantity","items":[{"productId":1}]}""")]));
        anthropic.SetupSequence(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tool":"get_top_selling_products_summary","arguments":{"top":5,"startDate":"2024-01-01","endDate":"2024-12-31"}}""")
            .ReturnsAsync("Road Bike is the top seller.");
        var service = new McpChatService(anthropic.Object, toolExecutor.Object);

        var result = await service.AskAsync("What are the top selling products in 2024?");

        result.Tool.Should().Be("get_top_selling_products_summary");
        result.Message.Should().Contain("top seller");
        toolExecutor.Verify(executor => executor.ExecuteToolAsync("get_top_selling_products_summary", It.Is<IReadOnlyDictionary<string, object>>(args => IsSummaryRequest(args, 5, "2024-01-01", "2024-12-31")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskAsync_WithToolOutsideDefinitionAllowList_RejectsSelection()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var toolExecutor = new Mock<IMcpToolExecutor>(MockBehavior.Strict);
        toolExecutor.Setup(executor => executor.GetToolDefinitions()).Returns(ToolDefinitions);
        toolExecutor.Setup(executor => executor.ExecuteToolAsync("list_database_tables", It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpCallToolResponse([new McpContentText(Text: System.Text.Json.JsonSerializer.Serialize(Catalog))]));
        anthropic.Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"tool":"totally_unknown_tool"}""");
        var service = new McpChatService(anthropic.Object, toolExecutor.Object);

        var action = () => service.AskAsync("use unknown tool");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unsupported MCP tool*");
    }
}