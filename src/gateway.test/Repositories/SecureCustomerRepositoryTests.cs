using FluentAssertions;
using Moq;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded.Entities;
using EnterpriseAiGateway.Tests.Fixtures;
using Xunit;

namespace EnterpriseAiGateway.Tests.Repositories;

/// <summary>
/// Comprehensive unit tests for SecureCustomerRepository.
/// Tests PII masking, customer retrieval, and data protection boundaries.
/// </summary>
public class SecureCustomerRepositoryTests : IDisposable
{
    private readonly ScaffoldedInMemoryDatabaseFixture _fixture;

    public SecureCustomerRepositoryTests()
    {
        _fixture = new ScaffoldedInMemoryDatabaseFixture();
    }

    #region GetCustomerContextAsync Tests

    [Fact]
    public async Task GetCustomerContextAsync_WithValidCustomerId_ReturnsFormattedCustomerContext()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "John",
            LastName = "Doe",
            CompanyName = "Acme Corp",
            EmailAddress = "john.doe@example.com",
            Phone = "555-123-4567"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: false);

        // Assert
        result.Should()
            .Contain("John")
            .And.Contain("Doe")
            .And.Contain("Acme Corp")
            .And.Contain("john.doe@example.com")
            .And.Contain("555-123-4567");
        result.Should().StartWith("Customer Entity Record Detected");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithMaskingEnabled_RedactsEmailAddress()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Jane",
            LastName = "Smith",
            CompanyName = "TechCorp",
            EmailAddress = "jane.smith@techcorp.com",
            Phone = "555-987-6543"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_EMAIL]")
            .And.NotContain("jane.smith@techcorp.com");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithMaskingEnabled_RedactsPhoneNumber()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Bob",
            LastName = "Johnson",
            CompanyName = "RetailCo",
            EmailAddress = "bob@retail.com",
            Phone = "555-321-0987"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_PHONE]")
            .And.NotContain("555-321-0987");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithMaskingEnabled_RedactsBothEmailAndPhone()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Alice",
            LastName = "Williams",
            CompanyName = "CloudSystems",
            EmailAddress = "alice.williams@cloudsystems.io",
            Phone = "555-654-3210"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_EMAIL]")
            .And.Contain("[REDACTED_PHONE]")
            .And.NotContain("alice.williams@cloudsystems.io")
            .And.NotContain("555-654-3210")
            .And.NotContain("Alice")
            .And.NotContain("Williams")
            .And.NotContain("CloudSystems")
            .And.Contain("[REDACTED_NAME]")
            .And.Contain("[REDACTED_COMPANY]");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithNullEmail_DoesNotThrowWithMasking()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Charlie",
            LastName = "Brown",
            CompanyName = "NoEmailCorp",
            EmailAddress = null,
            Phone = "555-111-2222"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .NotBeNullOrEmpty()
            .And.Contain("[REDACTED_NAME]")
            .And.Contain("[REDACTED_PHONE]");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithNullPhone_DoesNotThrowWithMasking()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Diana",
            LastName = "Prince",
            CompanyName = "WonderCorp",
            EmailAddress = "diana@wonder.com",
            Phone = null
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .NotBeNullOrEmpty()
            .And.Contain("[REDACTED_EMAIL]")
            .And.Contain("[REDACTED_NAME]");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithEmptyEmail_DoesNotThrowWithMasking()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Eve",
            LastName = "Miller",
            CompanyName = "EmptyEmailCorp",
            EmailAddress = string.Empty,
            Phone = "555-999-8888"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .NotBeNullOrEmpty()
            .And.Contain("[REDACTED_NAME]")
            .And.Contain("[REDACTED_PHONE]");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithNonExistentCustomerId_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = _fixture.GetContext();
        var nonExistentCustomerId = 9999;
        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(nonExistentCustomerId, maskSensitiveData: false);

        // Assert
        result.Should()
            .Contain("System Alert")
            .And.Contain("not found")
            .And.Contain("9999");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithMultipleCustomers_ReturnsOnlyRequestedCustomer()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer1 = new Customer
        {
            CustomerID = 1,
            FirstName = "Frank",
            LastName = "Sinatra",
            CompanyName = "Music Inc",
            EmailAddress = "frank@music.com",
            Phone = "555-000-1111"
        };

        var customer2 = new Customer
        {
            CustomerID = 2,
            FirstName = "Gina",
            LastName = "Garcia",
            CompanyName = "Art Studio",
            EmailAddress = "gina@art.com",
            Phone = "555-222-3333"
        };

        context.Customers.AddRange(customer1, customer2);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(1, maskSensitiveData: false);

        // Assert
        result.Should()
            .Contain("Frank")
            .And.NotContain("Gina");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithoutMasking_DoesNotModifyOriginalData()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Henry",
            LastName = "Harrison",
            CompanyName = "HarrisonCo",
            EmailAddress = "henry@harrison.com",
            Phone = "555-444-5555"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: false);

        // Assert
        result.Should()
            .Contain("henry@harrison.com")
            .And.Contain("555-444-5555");
    }

    [Fact]
    public void GetCustomerContextAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SecureCustomerRepository(null!));
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithComplexEmail_MasksCorrectly()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Iris",
            LastName = "Irving",
            CompanyName = "ComplexEmailCorp",
            EmailAddress = "iris.irving.name+tag@sub.domain.co.uk",
            Phone = "555-666-7777"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_EMAIL]")
            .And.NotContain("iris.irving.name+tag@sub.domain.co.uk");
    }

    [Fact]
    public async Task GetCustomerContextAsync_WithCaseSensitiveEmail_MasksCorrectly()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Jack",
            LastName = "Jackson",
            CompanyName = "CaseSensitiveCorp",
            EmailAddress = "JACK.JACKSON@EXAMPLE.COM",
            Phone = "555-888-9999"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_EMAIL]")
            .And.NotContain("JACK.JACKSON@EXAMPLE.COM");
    }

    [Theory]
    [InlineData("555-123-4567")]
    [InlineData("555-987-6543")]
    [InlineData("000-000-0000")]
    [InlineData("999-999-9999")]
    public async Task GetCustomerContextAsync_WithVariousPhoneFormats_MasksAllCorrectly(string phoneNumber)
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Kevin",
            LastName = "King",
            CompanyName = "PhoneCorp",
            EmailAddress = "kevin@king.com",
            Phone = phoneNumber
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);

        // Act
        var result = await repository.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        // Assert
        result.Should()
            .Contain("[REDACTED_PHONE]")
            .And.NotContain(phoneNumber);
    }

    #endregion

    #region Concurrency and Thread Safety Tests

    [Fact]
    public async Task GetCustomerContextAsync_ConcurrentRequests_ProducesConsistentResults()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customerId = 1;
        var customer = new Customer
        {
            CustomerID = customerId,
            FirstName = "Laura",
            LastName = "Lewis",
            CompanyName = "ConcurrencyCorp",
            EmailAddress = "laura@concurrency.com",
            Phone = "555-111-3333"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var repository = new SecureCustomerRepository(context);
        var results = new List<string>();

        // Act - Execute multiple concurrent requests
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => repository.GetCustomerContextAsync(customerId, maskSensitiveData: true))
            .ToList();

        var resultArray = await Task.WhenAll(tasks);
        results.AddRange(resultArray);

        // Assert - All results should be identical
        results.Should().AllBe(results.First());
    }

    #endregion

    #region Mock Assertions Tests

    [Fact]
    public async Task GetCustomerContextAsync_VerifyDbContextQueryBehavior_WithMocks()
    {
        // Arrange
        var mockRepository = new Mock<ISecureCustomerRepository>(MockBehavior.Strict);
        var expectedResult = "Customer Entity Record Detected -> ID: 1 | Name: Mock Customer | Company: MockCorp | Contact: 555-000-0000 / mock@example.com";

        mockRepository
            .Setup(r => r.GetCustomerContextAsync(1, false))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await mockRepository.Object.GetCustomerContextAsync(1, false);

        // Assert
        result.Should().Be(expectedResult);
        mockRepository.Verify(r => r.GetCustomerContextAsync(1, false), Times.Once);
    }

    [Fact]
    public void SecureCustomerRepository_Constructor_ValidatesNullContext()
    {
        // Arrange & Act & Assert
        var action = () => new SecureCustomerRepository(null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    #endregion

    void IDisposable.Dispose()
    {
        _fixture?.Dispose();
    }
}
