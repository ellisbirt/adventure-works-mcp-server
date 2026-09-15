using FluentAssertions;
using Moq;
using EnterpriseAiGateway.Data.Repositories;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

/// <summary>
/// Unit tests for SecureCustomerRepository, which delegates entirely to the same
/// per-table column allow-list used by SecureTableCatalogRepository.
/// </summary>
public class SecureCustomerRepositoryTests
{
    private readonly Mock<ISecureTableCatalogRepository> _tableCatalog = new(MockBehavior.Strict);

    [Fact]
    public async Task GetCustomerContextAsync_WithValidCustomerId_ReturnsFormattedCustomerContext()
    {
        _tableCatalog
            .Setup(repository => repository.GetTableRowsAsync("SalesLT", "Customer", 1, "CustomerID", 1))
            .ReturnsAsync("""[{"CustomerID":1,"ModifiedDate":"2024-01-01T00:00:00"}]""");
        var repository = new SecureCustomerRepository(_tableCatalog.Object);

        var result = await repository.GetCustomerContextAsync(1);

        result.Should().StartWith("Customer Entity Record Detected").And.Contain("CustomerID: 1");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithNonExistentCustomerId_ReturnsNotFoundMessage()
    {
        _tableCatalog
            .Setup(repository => repository.GetTableRowsAsync("SalesLT", "Customer", 1, "CustomerID", 9999))
            .ReturnsAsync("[]");
        var repository = new SecureCustomerRepository(_tableCatalog.Object);

        var result = await repository.GetCustomerContextAsync(9999);

        result.Should().Contain("System Alert").And.Contain("not found").And.Contain("9999");
    }

    [Fact]
    public async Task GetCustomerContextAsync_NeverExposesColumnsOutsideTheSafeCatalog()
    {
        // The catalog is the single source of truth for what is safe; the repository must not
        // add its own name/company/email/phone fields regardless of what the catalog returns.
        _tableCatalog
            .Setup(repository => repository.GetTableRowsAsync("SalesLT", "Customer", 1, "CustomerID", 1))
            .ReturnsAsync("""[{"CustomerID":1,"ModifiedDate":"2024-01-01T00:00:00"}]""");
        var repository = new SecureCustomerRepository(_tableCatalog.Object);

        var result = await repository.GetCustomerContextAsync(1);

        result.Should().NotContain("FirstName").And.NotContain("EmailAddress").And.NotContain("Phone").And.NotContain("CompanyName");
    }

    [Fact]
    public async Task GetCustomerContextAsync_DelegatesToTheSharedTableCatalogWithExpectedArguments()
    {
        _tableCatalog
            .Setup(repository => repository.GetTableRowsAsync("SalesLT", "Customer", 1, "CustomerID", 7))
            .ReturnsAsync("[]");
        var repository = new SecureCustomerRepository(_tableCatalog.Object);

        await repository.GetCustomerContextAsync(7);

        _tableCatalog.Verify(
            repository => repository.GetTableRowsAsync("SalesLT", "Customer", 1, "CustomerID", 7),
            Times.Once);
    }

    [Fact]
    public void Constructor_ValidatesNullTableCatalog()
    {
        var action = () => new SecureCustomerRepository(null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("tableCatalog");
    }
}
