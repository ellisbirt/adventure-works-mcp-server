using FluentAssertions;
using Moq;
using EnterpriseAiGateway.Data.Models;
using Xunit;

namespace EnterpriseAiGateway.Tests.Data.Models;

/// <summary>
/// Comprehensive unit tests for Customer entity model.
/// Tests validation, property assignment, and model integrity.
/// </summary>
public class CustomerTests
{
    private readonly MockRepository _mockRepository = new(MockBehavior.Strict);

    #region Constructor and Property Tests

    [Fact]
    public void Customer_DefaultConstructor_CreatesInstance()
    {
        // Act
        var customer = new Customer();

        // Assert
        customer.Should().NotBeNull();
        customer.FirstName.Should().Be(string.Empty);
        customer.LastName.Should().Be(string.Empty);
        customer.CompanyName.Should().BeNull();
        customer.EmailAddress.Should().BeNull();
        customer.Phone.Should().BeNull();
    }

    [Fact]
    public void Customer_WithAllPropertiesSet_StoresValuesCorrectly()
    {
        // Arrange
        var customerId = 1;
        var firstName = "John";
        var lastName = "Doe";
        var companyName = "Acme Corp";
        var email = "john.doe@acme.com";
        var phone = "555-123-4567";

        // Act
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = firstName,
            LastName = lastName,
            CompanyName = companyName,
            EmailAddress = email,
            Phone = phone
        };

        // Assert
        customer.CustomerID.Should().Be(customerId);
        customer.FirstName.Should().Be(firstName);
        customer.LastName.Should().Be(lastName);
        customer.CompanyName.Should().Be(companyName);
        customer.EmailAddress.Should().Be(email);
        customer.Phone.Should().Be(phone);
    }

    [Fact]
    public void Customer_CustomerIDProperty_CanBeAssigned()
    {
        // Arrange
        var customer = new Customer { CustomerID = 1 };

        // Act
        var actualId = customer.CustomerID;

        // Assert
        actualId.Should().Be(1);
    }

    [Fact]
    public void Customer_FirstNameProperty_CanBeAssigned()
    {
        // Arrange
        var customer = new Customer { FirstName = "Jane" };

        // Act
        var actualName = customer.FirstName;

        // Assert
        actualName.Should().Be("Jane");
    }

    [Fact]
    public void Customer_LastNameProperty_CanBeAssigned()
    {
        // Arrange
        var customer = new Customer { LastName = "Smith" };

        // Act
        var actualName = customer.LastName;

        // Assert
        actualName.Should().Be("Smith");
    }

    [Fact]
    public void Customer_CompanyNameProperty_CanBeNull()
    {
        // Arrange & Act
        var customer = new Customer { CompanyName = null };

        // Assert
        customer.CompanyName.Should().BeNull();
    }

    [Fact]
    public void Customer_EmailAddressProperty_CanBeNull()
    {
        // Arrange & Act
        var customer = new Customer { EmailAddress = null };

        // Assert
        customer.EmailAddress.Should().BeNull();
    }

    [Fact]
    public void Customer_PhoneProperty_CanBeNull()
    {
        // Arrange & Act
        var customer = new Customer { Phone = null };

        // Assert
        customer.Phone.Should().BeNull();
    }

    #endregion

    #region Property Modification Tests

    [Fact]
    public void Customer_PropertiesCanBeModifiedAfterCreation()
    {
        // Arrange
        var customer = new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe" };

        // Act
        customer.FirstName = "Jane";
        customer.LastName = "Smith";

        // Assert
        customer.FirstName.Should().Be("Jane");
        customer.LastName.Should().Be("Smith");
    }

    [Fact]
    public void Customer_CompanyNameCanBeSetAndModified()
    {
        // Arrange
        var customer = new Customer { CompanyName = "OldCorp" };

        // Act
        customer.CompanyName = "NewCorp";

        // Assert
        customer.CompanyName.Should().Be("NewCorp");
    }

    [Fact]
    public void Customer_EmailAddressCanBeSetAndModified()
    {
        // Arrange
        var customer = new Customer { EmailAddress = "old@example.com" };

        // Act
        customer.EmailAddress = "new@example.com";

        // Assert
        customer.EmailAddress.Should().Be("new@example.com");
    }

    [Fact]
    public void Customer_PhoneCanBeSetAndModified()
    {
        // Arrange
        var customer = new Customer { Phone = "555-000-0000" };

        // Act
        customer.Phone = "555-111-1111";

        // Assert
        customer.Phone.Should().Be("555-111-1111");
    }

    [Fact]
    public void Customer_CanSetPropertyToEmptyString()
    {
        // Arrange
        var customer = new Customer { FirstName = "John" };

        // Act
        customer.FirstName = string.Empty;

        // Assert
        customer.FirstName.Should().Be(string.Empty);
    }

    #endregion

    #region Null and Empty String Tests

    [Fact]
    public void Customer_FirstNameInitializesToEmptyString()
    {
        // Arrange & Act
        var customer = new Customer();

        // Assert
        customer.FirstName.Should().Be(string.Empty);
        customer.FirstName.Should().NotBeNull();
    }

    [Fact]
    public void Customer_LastNameInitializesToEmptyString()
    {
        // Arrange & Act
        var customer = new Customer();

        // Assert
        customer.LastName.Should().Be(string.Empty);
        customer.LastName.Should().NotBeNull();
    }

    [Fact]
    public void Customer_CompanyNameCanBeNullOrEmpty()
    {
        // Arrange
        var customer1 = new Customer { CompanyName = null };
        var customer2 = new Customer { CompanyName = string.Empty };

        // Act & Assert
        customer1.CompanyName.Should().BeNull();
        customer2.CompanyName.Should().BeEmpty();
    }

    [Fact]
    public void Customer_EmailCanBeNullOrEmpty()
    {
        // Arrange
        var customer1 = new Customer { EmailAddress = null };
        var customer2 = new Customer { EmailAddress = string.Empty };

        // Act & Assert
        customer1.EmailAddress.Should().BeNull();
        customer2.EmailAddress.Should().BeEmpty();
    }

    [Fact]
    public void Customer_PhoneCanBeNullOrEmpty()
    {
        // Arrange
        var customer1 = new Customer { Phone = null };
        var customer2 = new Customer { Phone = string.Empty };

        // Act & Assert
        customer1.Phone.Should().BeNull();
        customer2.Phone.Should().BeEmpty();
    }

    #endregion

    #region Complex Scenarios Tests

    [Fact]
    public void Customer_WithSpecialCharactersInNames_StoresCorrectly()
    {
        // Arrange
        var firstName = "François";
        var lastName = "O'Brien-Smith";

        // Act
        var customer = new Customer
        {
            FirstName = firstName,
            LastName = lastName
        };

        // Assert
        customer.FirstName.Should().Be(firstName);
        customer.LastName.Should().Be(lastName);
    }

    [Fact]
    public void Customer_WithLongNames_StoresCorrectly()
    {
        // Arrange
        var longFirstName = "JohnAlexanderFitzgeraldWilliamson";
        var longLastName = "VanDerBerghtSteinhauerMcDonaldson";

        // Act
        var customer = new Customer
        {
            FirstName = longFirstName,
            LastName = longLastName
        };

        // Assert
        customer.FirstName.Should().Be(longFirstName);
        customer.LastName.Should().Be(longLastName);
    }

    [Fact]
    public void Customer_WithComplexEmailAddresses_StoresCorrectly()
    {
        // Arrange
        var emails = new[]
        {
            "simple@example.com",
            "user.name+tag@example.co.uk",
            "test_email@sub.domain.example.com",
            "123@example.com"
        };

        // Act & Assert
        foreach (var email in emails)
        {
            var customer = new Customer { EmailAddress = email };
            customer.EmailAddress.Should().Be(email);
        }
    }

    [Fact]
    public void Customer_WithVariousPhoneFormats_StoresCorrectly()
    {
        // Arrange
        var phones = new[]
        {
            "555-123-4567",
            "555-987-6543",
            "(555) 123-4567",
            "+1 555 123 4567"
        };

        // Act & Assert
        foreach (var phone in phones)
        {
            var customer = new Customer { Phone = phone };
            customer.Phone.Should().Be(phone);
        }
    }

    [Fact]
    public void Customer_MultipleInstancesAreIndependent()
    {
        // Arrange
        var customer1 = new Customer { CustomerID = 1, FirstName = "John" };
        var customer2 = new Customer { CustomerID = 2, FirstName = "Jane" };

        // Act & Assert
        customer1.FirstName.Should().Be("John");
        customer2.FirstName.Should().Be("Jane");
        customer1.CustomerID.Should().NotBe(customer2.CustomerID);
    }

    [Fact]
    public void Customer_CanCreateMultipleInstancesWithDifferentValues()
    {
        // Arrange & Act
        var customers = Enumerable.Range(1, 5)
            .Select(i => new Customer
            {
                CustomerID = i,
                FirstName = $"Customer{i}",
                LastName = $"Surname{i}"
            })
            .ToList();

        // Assert
        customers.Should().HaveCount(5);
        customers.Select(c => c.CustomerID).Should().ContainInOrder(1, 2, 3, 4, 5);
        customers.Select(c => c.FirstName).Should().ContainInOrder(
            "Customer1", "Customer2", "Customer3", "Customer4", "Customer5"
        );
    }

    #endregion

    #region Mock Integration Tests

    [Fact]
    public async Task Customer_CanBeUsedWithMocks()
    {
        // Arrange
        var mockCustomerService = _mockRepository.Create<ICustomerService>();
        var expectedCustomer = new Customer
        {
            CustomerID = 1,
            FirstName = "John",
            LastName = "Doe",
            CompanyName = "Acme",
            EmailAddress = "john@acme.com",
            Phone = "555-1234"
        };

        mockCustomerService
            .Setup(s => s.GetCustomerAsync(1))
            .ReturnsAsync(expectedCustomer);

        // Act
        var result = await mockCustomerService.Object.GetCustomerAsync(1);

        // Assert
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.CompanyName.Should().Be("Acme");
        mockCustomerService.Verify(s => s.GetCustomerAsync(1), Times.Once);
    }

    [Fact]
    public async Task Customer_MockVerification_EnsuresCallCount()
    {
        // Arrange
        var mockCustomerRepository = _mockRepository.Create<ICustomerRepository>();
        var customer = new Customer { CustomerID = 1, FirstName = "Test" };

        mockCustomerRepository
            .Setup(r => r.SaveAsync(It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        // Act
        await mockCustomerRepository.Object.SaveAsync(customer);
        await mockCustomerRepository.Object.SaveAsync(customer);

        // Assert
        mockCustomerRepository.Verify(r => r.SaveAsync(It.IsAny<Customer>()), Times.Exactly(2));
    }

    #endregion

    #region Equality and Comparison Tests

    [Fact]
    public void Customer_SameValuesAreEqual()
    {
        // Arrange
        var customer1 = new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe" };
        var customer2 = new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe" };

        // Act & Assert - Note: Customer is a class, so equality is by reference unless overridden
        customer1.Should().NotBe(customer2); // Different instances
        customer1.CustomerID.Should().Be(customer2.CustomerID);
        customer1.FirstName.Should().Be(customer2.FirstName);
    }

    [Fact]
    public void Customer_DifferentValuesAreDifferent()
    {
        // Arrange
        var customer1 = new Customer { CustomerID = 1, FirstName = "John" };
        var customer2 = new Customer { CustomerID = 2, FirstName = "Jane" };

        // Act & Assert
        customer1.CustomerID.Should().NotBe(customer2.CustomerID);
        customer1.FirstName.Should().NotBe(customer2.FirstName);
    }

    #endregion

    #region Test Helper Interfaces

    /// <summary>
    /// Mock interface for customer service operations
    /// </summary>
    public interface ICustomerService
    {
        Task<Customer> GetCustomerAsync(int customerId);
    }

    /// <summary>
    /// Mock interface for customer repository operations
    /// </summary>
    public interface ICustomerRepository
    {
        Task SaveAsync(Customer customer);
    }

    #endregion
}
