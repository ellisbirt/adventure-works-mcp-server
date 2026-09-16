using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AwesomeAssertions;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using EnterpriseAiGateway.Data.Scaffolded.Entities;
using Microsoft.Data.SqlClient;
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

    [Fact]
    public async Task GetTopSellingProductsSummaryAsync_UsesRelationalRankingAndFilters()
    {
        var connection = new CapturingDbConnection(
        [
            new object?[] { 2, "Helmet", "HM-1", 11, "Accessories", 5, 300m, 2 }
        ]);
        await using var context = CreateRelationalContext(connection);
        var repository = new SecureSalesSummaryRepository(context);

        var result = await repository.GetTopSellingProductsSummaryAsync(new SalesSummaryFilter(
            StartDate: new DateOnly(2024, 1, 1),
            EndDate: new DateOnly(2024, 12, 31),
            ProductIds: [2, 1],
            ProductCategoryIds: [11],
            Top: 2));
        var document = JsonDocument.Parse(result);

        document.RootElement.GetProperty("items")[0].GetProperty("productId").GetInt32().Should().Be(2);
        connection.LastCommand.Should().NotBeNull();
        connection.LastCommand!.CommandText.Should().Contain("ORDER BY TotalQuantitySold DESC, TotalRevenue DESC, d.ProductID ASC");
        connection.LastCommand.CommandText.Should().Contain("WHERE h.OrderDate >= @startDate AND h.OrderDate <= @endDate AND d.ProductID IN (@productId0, @productId1) AND p.ProductCategoryID IN (@productCategoryId0)");
        var parameters = connection.LastCommand.Parameters.Cast<SqlParameter>()
            .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value);
        parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["@startDate"] = new DateOnly(2024, 1, 1).ToDateTime(TimeOnly.MinValue),
            ["@endDate"] = new DateOnly(2024, 12, 31).ToDateTime(TimeOnly.MaxValue),
            ["@productId0"] = 1,
            ["@productId1"] = 2,
            ["@productCategoryId0"] = 11,
            ["@top"] = 2
        });
    }

    [Fact]
    public async Task GetHighestRevenueProductsSummaryAsync_UsesRelationalRevenueRanking()
    {
        var connection = new CapturingDbConnection(
        [
            new object?[] { 1, "Road Bike", "RB-1", 10, "Bikes", 2, 1000m, 1 }
        ]);
        await using var context = CreateRelationalContext(connection);
        var repository = new SecureSalesSummaryRepository(context);

        var result = await repository.GetHighestRevenueProductsSummaryAsync(new SalesSummaryFilter(Top: 1));
        var document = JsonDocument.Parse(result);

        document.RootElement.GetProperty("items")[0].GetProperty("totalRevenue").GetDecimal().Should().Be(1000m);
        connection.LastCommand.Should().NotBeNull();
        connection.LastCommand!.CommandText.Should().Contain("ORDER BY TotalRevenue DESC, TotalQuantitySold DESC, d.ProductID ASC");
    }

    private static AdventureWorksDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdventureWorksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AdventureWorksDbContext(options);
    }

    private static AdventureWorksDbContext CreateRelationalContext(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<AdventureWorksDbContext>()
            .UseSqlServer(connection)
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

    private sealed class CapturingDbConnection(params IReadOnlyList<object?[]> rows) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public CapturingDbCommand? LastCommand { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "AdventureWorks";

        public override string DataSource => "Test";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            LastCommand = new CapturingDbCommand(this, rows);
            return LastCommand;
        }
    }

    private sealed class CapturingDbCommand(CapturingDbConnection connection, IReadOnlyList<object?[]> rows) : DbCommand
    {
        private readonly SqlCommand _inner = new();

        [AllowNull]
        public override string CommandText
        {
            get => _inner.CommandText;
            set => _inner.CommandText = value ?? string.Empty;
        }

        public override int CommandTimeout
        {
            get => _inner.CommandTimeout;
            set => _inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _inner.CommandType;
            set => _inner.CommandType = value;
        }

        public override bool DesignTimeVisible
        {
            get => _inner.DesignTimeVisible;
            set => _inner.DesignTimeVisible = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _inner.UpdatedRowSource;
            set => _inner.UpdatedRowSource = value;
        }

        protected override DbConnection? DbConnection
        {
            get => connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new SqlParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new CapturingDbDataReader(rows);
    }

    private sealed class CapturingDbDataReader(IReadOnlyList<object?[]> rows) : DbDataReader
    {
        private static readonly string[] ColumnNames =
        [
            "ProductId",
            "ProductName",
            "ProductNumber",
            "ProductCategoryId",
            "ProductCategoryName",
            "TotalQuantitySold",
            "TotalRevenue",
            "OrderCount"
        ];

        private int _index = -1;

        public override int FieldCount => ColumnNames.Length;

        public override bool HasRows => rows.Count > 0;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override int Depth => 0;

        public override bool IsClosed => false;

        public override int RecordsAffected => 0;

        public override bool Read() => ++_index < rows.Count;

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public override string GetName(int ordinal) => ColumnNames[ordinal];

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override Type GetFieldType(int ordinal) => ordinal switch
        {
            0 or 5 or 7 => typeof(int),
            1 or 2 or 4 => typeof(string),
            3 => typeof(int),
            6 => typeof(decimal),
            _ => typeof(object)
        };

        public override object GetValue(int ordinal) => rows[_index][ordinal] ?? DBNull.Value;

        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var ordinal = 0; ordinal < count; ordinal++)
                values[ordinal] = GetValue(ordinal);

            return count;
        }

        public override int GetOrdinal(string name) => Array.IndexOf(ColumnNames, name);

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        public override char GetChar(int ordinal) => (char)GetValue(ordinal);

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

        public override IEnumerator GetEnumerator() => rows.GetEnumerator();
    }
}
