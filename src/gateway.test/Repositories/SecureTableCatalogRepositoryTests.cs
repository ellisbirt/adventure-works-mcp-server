using EnterpriseAiGateway.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

public class SecureTableCatalogRepositoryTests
{
    [Theory]
    [InlineData("Customer", "PasswordHash")]
    [InlineData("Customer", "PasswordSalt")]
    [InlineData("Customer", "Rowguid")]
    [InlineData("Address", "Rowguid")]
    [InlineData("SalesOrderHeader", "Rowguid")]
    public void IsAllowedColumn_BlocksCredentialAndInternalFieldsOnTheirOwningTable(string table, string columnName)
    {
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", table, columnName).Should().BeFalse();
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
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", table, columnName).Should().BeTrue();
        SecureTableCatalogRepository.IsPiiColumn("SalesLT", table, columnName).Should().BeTrue();
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
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", table, columnName).Should().BeTrue();
        SecureTableCatalogRepository.IsPiiColumn("SalesLT", table, columnName).Should().BeFalse();
    }

    [Fact]
    public void IsAllowedColumn_ScopesTheSameColumnNamePerTable()
    {
        // "CustomerID" is a safe foreign key on Customer/SalesOrderHeader but must not
        // leak onto a table where it was never vetted, like Product.
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", "Customer", "CustomerID").Should().BeTrue();
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", "Product", "CustomerID").Should().BeFalse();
    }

    [Fact]
    public void IsAllowedColumn_RejectsTablesThatAreNotExplicitlyMapped()
    {
        SecureTableCatalogRepository.IsAllowedColumn("SalesLT", "FutureTable", "Name").Should().BeFalse();
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