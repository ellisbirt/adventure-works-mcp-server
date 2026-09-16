using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

public class SecureTableCatalogRepositoryTests
{
    private readonly IModel _model;

    public SecureTableCatalogRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AdventureWorksDbContext>()
            .UseInMemoryDatabase(nameof(SecureTableCatalogRepositoryTests))
            .Options;
        using var context = new AdventureWorksDbContext(options);
        _model = context.Model;
    }

    [Theory]
    [InlineData("Customer", "PasswordHash")]
    [InlineData("Customer", "PasswordSalt")]
    [InlineData("Customer", "Rowguid")]
    [InlineData("Address", "Rowguid")]
    [InlineData("SalesOrderHeader", "Rowguid")]
    public void IsAllowedColumn_BlocksCredentialAndInternalFieldsOnTheirOwningTable(string table, string columnName)
    {
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", table, columnName).Should().BeFalse();
    }

    [Theory]
    [InlineData("Customer", "FirstName")]
    [InlineData("Customer", "LastName")]
    [InlineData("Customer", "EmailAddress")]
    [InlineData("Customer", "Phone")]
    [InlineData("Customer", "CompanyName")]
    [InlineData("Address", "AddressLine1")]
    [InlineData("Address", "PostalCode")]
    [InlineData("SalesOrderHeader", "CreditCardApprovalCode")]
    public void IsPiiColumn_MarksPersonalFieldsForRedactionButStillAllowsThem(string table, string columnName)
    {
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", table, columnName).Should().BeTrue();
        SecureTableCatalogRepository.IsPiiColumn(_model, "SalesLT", table, columnName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Customer", "CustomerID")]
    [InlineData("Product", "ProductID")]
    [InlineData("Product", "Name")]
    [InlineData("Product", "ListPrice")]
    [InlineData("SalesOrderHeader", "Status")]
    [InlineData("Address", "City")]
    public void IsPiiColumn_AllowsNonPersonalBusinessFieldsOnTheirOwningTable(string table, string columnName)
    {
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", table, columnName).Should().BeTrue();
        SecureTableCatalogRepository.IsPiiColumn(_model, "SalesLT", table, columnName).Should().BeFalse();
    }

    [Fact]
    public void IsAllowedColumn_ScopesTheSameColumnNamePerTable()
    {
        // "CustomerID" is a safe foreign key on Customer/SalesOrderHeader but must not
        // leak onto a table where it was never vetted, like Product.
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", "Customer", "CustomerID").Should().BeTrue();
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", "Product", "CustomerID").Should().BeFalse();
    }

    [Fact]
    public void IsAllowedColumn_RejectsTablesThatAreNotExplicitlyMapped()
    {
        SecureTableCatalogRepository.IsAllowedColumn(_model, "SalesLT", "FutureTable", "Name").Should().BeFalse();
    }

    [Fact]
    public void RedactPiiColumns_ReplacesOnlyThePiiColumnsWithTheRedactedMarker()
    {
        var row = new Dictionary<string, object?>
        {
            ["CustomerID"] = 1,
            ["FirstName"] = "Jane",
            ["EmailAddress"] = "jane@example.com",
        };

        SecureTableCatalogRepository.RedactPiiColumns(row, ["FirstName", "EmailAddress"]);

        row["CustomerID"].Should().Be(1);
        row["FirstName"].Should().Be(SecureTableCatalogRepository.RedactedMarker);
        row["EmailAddress"].Should().Be(SecureTableCatalogRepository.RedactedMarker);
    }
}