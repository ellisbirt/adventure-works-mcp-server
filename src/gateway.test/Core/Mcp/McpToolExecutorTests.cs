using AwesomeAssertions;
using EnterpriseAiGateway.Core.Mcp;
using EnterpriseAiGateway.Data.Repositories;
using System.Text.Json;
using Moq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnterpriseAiGateway.Tests.Core.Mcp;

public class McpToolExecutorTests
{
    private readonly Mock<ISecureCustomerRepository> _customerRepository = new(MockBehavior.Strict);
    private readonly Mock<ISecureTableCatalogRepository> _tableCatalog = new(MockBehavior.Strict);
    private readonly Mock<ISecureSalesInsightsRepository> _salesInsightsRepository = new(MockBehavior.Strict);
    private readonly Mock<ILogger<McpToolExecutor>> _logger = new(MockBehavior.Loose);

    [Fact]
    public void GetToolDefinitions_IncludesSalesInsightTools()
    {
        var executor = CreateExecutor();

        var tools = executor.GetToolDefinitions();

        tools.Should().HaveCount(10);
        tools.Select(tool => tool.Name).Should().Contain([
            "get_product_profitability_summary",
            "get_sales_trend_summary",
            "get_customer_value_summary",
            "get_product_mix_summary",
            "get_geography_channel_summary",
            "get_inventory_risk_summary",
            "get_demand_forecast_summary"
        ]);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithSalesInsightTool_DelegatesWithParsedOptions()
    {
        _salesInsightsRepository
            .Setup(repository => repository.GetProductProfitabilitySummaryAsync(It.Is<SalesInsightFilterOptions>(options =>
                options.Top == 5 &&
                options.StartDate == new DateOnly(2024, 1, 1) &&
                options.EndDate == new DateOnly(2024, 2, 1) &&
                options.ProductIds!.SequenceEqual([1, 2]) &&
                options.ProductCategoryIds!.SequenceEqual([3]))))
            .ReturnsAsync("[{\"ProductId\":1}]");
        var executor = CreateExecutor();

        var response = await executor.ExecuteToolAsync("get_product_profitability_summary", new Dictionary<string, object>
        {
            ["top"] = Json("5"),
            ["startDate"] = Json("\"2024-01-01\""),
            ["endDate"] = Json("\"2024-02-01\""),
            ["productIds"] = Json("[1,2]"),
            ["productCategoryIds"] = Json("[3]")
        });

        response.IsError.Should().BeFalse();
        response.Content[0].Text.Should().Contain("ProductId");
    }

    [Fact]
    public async Task ExecuteToolAsync_WithInvalidTop_ReturnsError()
    {
        var executor = CreateExecutor();

        var response = await executor.ExecuteToolAsync("get_product_profitability_summary", new Dictionary<string, object>
        {
            ["top"] = Json("0")
        });

        response.IsError.Should().BeTrue();
        response.Content[0].Text.Should().Contain("top");
        _salesInsightsRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithInvalidDateRange_ReturnsError()
    {
        var executor = CreateExecutor();

        var response = await executor.ExecuteToolAsync("get_sales_trend_summary", new Dictionary<string, object>
        {
            ["startDate"] = Json("\"2024-03-01\""),
            ["endDate"] = Json("\"2024-02-01\"")
        });

        response.IsError.Should().BeTrue();
        response.Content[0].Text.Should().Contain("startDate");
    }

    private McpToolExecutor CreateExecutor() =>
        new(_customerRepository.Object, _tableCatalog.Object, _salesInsightsRepository.Object, _logger.Object);

    private static object Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
