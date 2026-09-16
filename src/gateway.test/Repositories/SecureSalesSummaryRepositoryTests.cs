using System.Text.Json;
using AwesomeAssertions;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using EnterpriseAiGateway.Data.Scaffolded.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

public class SecureSalesSummaryRepositoryTests
{
    [Fact]
    public async Task GetTopSellingProductsSummaryAsync_RanksByQuantityAndAppliesTop()
    {
        await using var context = CreateContext();
        SeedSalesData(context);
        var repository = new SecureSalesSummaryRepository(context);

        var result = await repository.GetTopSellingProductsSummaryAsync(new SalesSummaryFilter(Top: 1));
        var document = JsonDocument.Parse(result);

        document.RootElement.GetProperty("metric").GetString().Should().Be("top_selling_products_by_quantity");
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("productId").GetInt32().Should().Be(2);
        items[0].GetProperty("totalQuantitySold").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task GetHighestRevenueProductsSummaryAsync_RanksByRevenue()
    {
        await using var context = CreateContext();
        SeedSalesData(context);
        var repository = new SecureSalesSummaryRepository(context);

        var result = await repository.GetHighestRevenueProductsSummaryAsync(new SalesSummaryFilter(Top: 2));
        var document = JsonDocument.Parse(result);

        document.RootElement.GetProperty("metric").GetString().Should().Be("highest_revenue_products");
        var items = document.RootElement.GetProperty("items");
        items[0].GetProperty("productId").GetInt32().Should().Be(1);
        items[0].GetProperty("totalRevenue").GetDecimal().Should().Be(1000m);
    }

    [Fact]
    public async Task GetTopSellingProductsSummaryAsync_AppliesDateAndCategoryFilters()
    {
        await using var context = CreateContext();
        SeedSalesData(context);
        var repository = new SecureSalesSummaryRepository(context);

        var result = await repository.GetTopSellingProductsSummaryAsync(new SalesSummaryFilter(
            StartDate: new DateOnly(2024, 1, 1),
            EndDate: new DateOnly(2024, 12, 31),
            ProductCategoryIds: [10],
            Top: 10));
        var document = JsonDocument.Parse(result);
        var items = document.RootElement.GetProperty("items");

        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("productId").GetInt32().Should().Be(1);
    }

    private static AdventureWorksDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdventureWorksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AdventureWorksDbContext(options);
    }

    private static void SeedSalesData(AdventureWorksDbContext context)
    {
        context.ProductCategories.AddRange(
            new ProductCategory { ProductCategoryId = 10, Name = "Bikes", Rowguid = Guid.NewGuid(), ModifiedDate = DateTime.UtcNow },
            new ProductCategory { ProductCategoryId = 11, Name = "Accessories", Rowguid = Guid.NewGuid(), ModifiedDate = DateTime.UtcNow });

        context.Products.AddRange(
            new Product
            {
                ProductId = 1,
                Name = "Road Bike",
                ProductNumber = "RB-1",
                ProductCategoryId = 10,
                StandardCost = 100m,
                ListPrice = 500m,
                SellStartDate = DateTime.UtcNow,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            },
            new Product
            {
                ProductId = 2,
                Name = "Helmet",
                ProductNumber = "HM-1",
                ProductCategoryId = 11,
                StandardCost = 20m,
                ListPrice = 60m,
                SellStartDate = DateTime.UtcNow,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            });

        context.SalesOrderHeaders.AddRange(
            new SalesOrderHeader
            {
                SalesOrderId = 100,
                RevisionNumber = 1,
                OrderDate = new DateTime(2024, 5, 10),
                DueDate = new DateTime(2024, 5, 20),
                Status = 1,
                OnlineOrderFlag = true,
                SalesOrderNumber = "SO100",
                CustomerId = 1,
                ShipMethod = "UPS",
                SubTotal = 1100m,
                TaxAmt = 0m,
                Freight = 0m,
                TotalDue = 1100m,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            },
            new SalesOrderHeader
            {
                SalesOrderId = 101,
                RevisionNumber = 1,
                OrderDate = new DateTime(2023, 12, 20),
                DueDate = new DateTime(2023, 12, 30),
                Status = 1,
                OnlineOrderFlag = true,
                SalesOrderNumber = "SO101",
                CustomerId = 1,
                ShipMethod = "UPS",
                SubTotal = 300m,
                TaxAmt = 0m,
                Freight = 0m,
                TotalDue = 300m,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            });

        context.SalesOrderDetails.AddRange(
            new SalesOrderDetail
            {
                SalesOrderId = 100,
                SalesOrderDetailId = 1,
                ProductId = 1,
                OrderQty = 2,
                UnitPrice = 500m,
                UnitPriceDiscount = 0m,
                LineTotal = 1000m,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            },
            new SalesOrderDetail
            {
                SalesOrderId = 100,
                SalesOrderDetailId = 2,
                ProductId = 2,
                OrderQty = 1,
                UnitPrice = 60m,
                UnitPriceDiscount = 0m,
                LineTotal = 60m,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            },
            new SalesOrderDetail
            {
                SalesOrderId = 101,
                SalesOrderDetailId = 1,
                ProductId = 2,
                OrderQty = 4,
                UnitPrice = 60m,
                UnitPriceDiscount = 0m,
                LineTotal = 240m,
                Rowguid = Guid.NewGuid(),
                ModifiedDate = DateTime.UtcNow
            });

        context.SaveChanges();
    }
}
