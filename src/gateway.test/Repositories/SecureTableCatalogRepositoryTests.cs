using EnterpriseAiGateway.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

public class SecureTableCatalogRepositoryTests
{
    [Theory]
    [InlineData("FirstName")]
    [InlineData("LastName")]
    [InlineData("MiddleName")]
    [InlineData("EmailAddress")]
    [InlineData("Phone")]
    [InlineData("AddressLine1")]
    [InlineData("City")]
    [InlineData("PostalCode")]
    [InlineData("PasswordHash")]
    [InlineData("DateOfBirth")]
    [InlineData("SocialSecurityNumber")]
    [InlineData("CreditCardNumber")]
    [InlineData("ApiToken")]
    [InlineData("User_Name")]
    [InlineData("IPAddress")]
    [InlineData("NationalIdentifier")]
    [InlineData("DateOfBirthUtc")]
    [InlineData("FutureSensitiveColumn")]
    public void IsSensitiveColumn_RecognizesPersonalAndCredentialFields(string columnName)
    {
        SecureTableCatalogRepository.IsSensitiveColumn(columnName).Should().BeTrue();
    }

    [Theory]
    [InlineData("CustomerID")]
    [InlineData("ProductID")]
    [InlineData("Name")]
    [InlineData("ListPrice")]
    public void IsSensitiveColumn_AllowsNonPersonalBusinessFields(string columnName)
    {
        SecureTableCatalogRepository.IsSensitiveColumn(columnName).Should().BeFalse();
    }
}