using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Integration.Anthropic;
using EnterpriseAiGateway.Integration.Chat;
using FluentAssertions;
using Moq;
using Xunit;

namespace EnterpriseAiGateway.Tests.Integration.Chat;

public class McpChatServiceTests
{
    private static readonly IReadOnlyList<SafeTableDefinition> Catalog =
    [new("SalesLT", "Product", new[] { "ProductID", "Name", "ListPrice" })];

    [Fact]
    public async Task AskAsync_WithTableRequest_ExecutesOnlyCatalogApprovedToolAndReturnsAnswer()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var catalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        catalog.Setup(repository => repository.GetTablesAsync()).ReturnsAsync(Catalog);
        catalog.Setup(repository => repository.GetTableRowsAsync("SalesLT", "Product", 2))
            .ReturnsAsync("[{\"ProductID\":1,\"Name\":\"Road Bike\"}]");
        anthropic.SetupSequence(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tool\":\"read_database_table\",\"schema\":\"SalesLT\",\"table\":\"Product\",\"limit\":2}")
            .ReturnsAsync("Road Bike is available in the product catalog.");
        var service = new McpChatService(anthropic.Object, catalog.Object);

        var result = await service.AskAsync("Tell me about Road Bike");

        result.Tool.Should().Be("read_database_table");
        result.Message.Should().Contain("Road Bike");
        catalog.Verify(repository => repository.GetTableRowsAsync("SalesLT", "Product", 2), Times.Once);
        anthropic.Verify(client => client.SendMessageAsync(
            It.IsAny<string>(),
            It.Is<string>(input => input.Contains("MCP result: [{\"ProductID\":1,\"Name\":\"Road Bike\"}]")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskAsync_WithUnknownTable_RejectsTheModelSelection()
    {
        var anthropic = new Mock<IAnthropicClient>(MockBehavior.Strict);
        var catalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        catalog.Setup(repository => repository.GetTablesAsync()).ReturnsAsync(Catalog);
        anthropic.Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tool\":\"read_database_table\",\"schema\":\"SalesLT\",\"table\":\"Address\",\"limit\":20}");
        var service = new McpChatService(anthropic.Object, catalog.Object);

        var action = () => service.AskAsync("Show addresses");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the safe MCP catalog*");
        catalog.Verify(repository => repository.GetTableRowsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}