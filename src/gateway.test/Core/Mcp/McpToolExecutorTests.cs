using System.Text.Json;
using AwesomeAssertions;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Core.Mcp;
using EnterpriseAiGateway.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EnterpriseAiGateway.Tests.Core.Mcp;

public class McpToolExecutorTests
{
    [Fact]
    public async Task ExecuteToolAsync_WithTopSellingSummaryTool_UsesSummaryRepository()
    {
        var customerRepository = new Mock<ISecureCustomerRepository>(MockBehavior.Strict);
        var tableCatalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        var summaryRepository = new Mock<ISecureSalesSummaryRepository>(MockBehavior.Strict);
        summaryRepository
            .Setup(repository => repository.GetTopSellingProductsSummaryAsync(
                It.Is<SalesSummaryFilter>(filter =>
                    filter.Top == 5 &&
                    filter.StartDate == new DateOnly(2024, 1, 1) &&
                    filter.EndDate == new DateOnly(2024, 12, 31) &&
                    filter.ProductIds!.SequenceEqual(new[] { 1, 2 }) &&
                    filter.ProductCategoryIds!.SequenceEqual(new[] { 10 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"metric":"top_selling_products_by_quantity"}""");
        var executor = new McpToolExecutor(customerRepository.Object, summaryRepository.Object, tableCatalog.Object, NullLogger<McpToolExecutor>.Instance);

        using var productIdsDocument = JsonDocument.Parse("[1,2]");
        using var categoryIdsDocument = JsonDocument.Parse("[10]");
        var result = await executor.ExecuteToolAsync("get_top_selling_products_summary", new Dictionary<string, object>
        {
            ["top"] = 5,
            ["startDate"] = "2024-01-01",
            ["endDate"] = "2024-12-31",
            ["productIds"] = productIdsDocument.RootElement.Clone(),
            ["productCategoryIds"] = categoryIdsDocument.RootElement.Clone()
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("top_selling_products_by_quantity");
        summaryRepository.VerifyAll();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithInvalidSummaryDates_ReturnsValidationError()
    {
        var customerRepository = new Mock<ISecureCustomerRepository>(MockBehavior.Strict);
        var tableCatalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        var summaryRepository = new Mock<ISecureSalesSummaryRepository>(MockBehavior.Strict);
        var executor = new McpToolExecutor(customerRepository.Object, summaryRepository.Object, tableCatalog.Object, NullLogger<McpToolExecutor>.Instance);

        var result = await executor.ExecuteToolAsync("get_highest_revenue_products_summary", new Dictionary<string, object>
        {
            ["startDate"] = "2025-01-01",
            ["endDate"] = "2024-01-01"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("startDate must be on or before endDate");
    }

    [Fact]
    public async Task ExecuteToolAsync_WithTooManySummaryFilterIds_ReturnsValidationError()
    {
        var customerRepository = new Mock<ISecureCustomerRepository>(MockBehavior.Strict);
        var tableCatalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        var summaryRepository = new Mock<ISecureSalesSummaryRepository>(MockBehavior.Strict);
        var executor = new McpToolExecutor(customerRepository.Object, summaryRepository.Object, tableCatalog.Object, NullLogger<McpToolExecutor>.Instance);

        using var productIdsDocument = JsonDocument.Parse($"[{string.Join(",", Enumerable.Range(1, 2098))}]");
        var result = await executor.ExecuteToolAsync("get_top_selling_products_summary", new Dictionary<string, object>
        {
            ["productIds"] = productIdsDocument.RootElement.Clone()
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("productIds and productCategoryIds can contain at most 2097 total values");
    }

    [Fact]
    public void GetToolDefinitions_ContainsSalesSummaryTools()
    {
        var customerRepository = new Mock<ISecureCustomerRepository>(MockBehavior.Strict);
        var tableCatalog = new Mock<ISecureTableCatalogRepository>(MockBehavior.Strict);
        var summaryRepository = new Mock<ISecureSalesSummaryRepository>(MockBehavior.Strict);
        var executor = new McpToolExecutor(customerRepository.Object, summaryRepository.Object, tableCatalog.Object, NullLogger<McpToolExecutor>.Instance);

        var tools = executor.GetToolDefinitions().Select(tool => tool.Name).ToList();

        tools.Should().Contain("get_top_selling_products_summary");
        tools.Should().Contain("get_highest_revenue_products_summary");
    }
}
