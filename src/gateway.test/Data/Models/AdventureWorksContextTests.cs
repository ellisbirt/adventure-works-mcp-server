using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using EnterpriseAiGateway.Data.Models;
using EnterpriseAiGateway.Tests.Fixtures;
using Xunit;

namespace EnterpriseAiGateway.Tests.Data.Models;

/// <summary>
/// Comprehensive unit tests for AdventureWorksContext DbContext.
/// Tests database configuration, entity mapping, and LINQ operations.
/// </summary>
public class AdventureWorksContextTests : IDisposable
{
    private readonly InMemoryDatabaseFixture _fixture;

    public AdventureWorksContextTests()
    {
        _fixture = new InMemoryDatabaseFixture();
    }

    #region Context Creation and Configuration Tests

    [Fact]
    public void AdventureWorksContext_WithValidOptions_CreatesInstance()
    {
        // Arrange & Act
        var context = _fixture.GetContext();

        // Assert
        context.Should().NotBeNull();
        context.Customers.Should().NotBeNull();
    }

    [Fact]
    public void AdventureWorksContext_HasCustomersDbSet()
    {
        // Arrange
        var context = _fixture.GetContext();

        // Act & Assert
        context.Customers.Should().NotBeNull();
    }

    #endregion

    #region Entity Mapping Tests

    [Fact]
    public async Task AdventureWorksContext_CustomerEntity_MapsToCorrectTable()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Test",
            LastName = "User",
            CompanyName = "TestCorp",
            EmailAddress = "test@example.com",
            Phone = "555-0000"
        };

        // Act
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FirstOrDefaultAsync(c => c.CustomerID == 1);
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Test");
        retrieved.LastName.Should().Be("User");
    }

    [Fact]
    public async Task AdventureWorksContext_CustomerEntityConfiguration_ValidatesRequiredFields()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "John",
            LastName = "Doe"
            // CompanyName, EmailAddress, Phone are optional
        };

        // Act
        context.Customers.Add(customer);
        var saveResult = await context.SaveChangesAsync();

        // Assert
        saveResult.Should().BeGreaterThan(0);
        var retrieved = await context.Customers.FindAsync(1);
        retrieved.Should().NotBeNull();
        retrieved!.CompanyName.Should().BeNull();
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task AdventureWorksContext_Query_FindsCustomerById()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "John",
            LastName = "Doe"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Customers.FirstOrDefaultAsync(c => c.CustomerID == 1);

        // Assert
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("John");
    }

    [Fact]
    public async Task AdventureWorksContext_Query_ReturnsNullForNonExistentCustomer()
    {
        // Arrange
        var context = _fixture.GetContext();

        // Act
        var result = await context.Customers.FirstOrDefaultAsync(c => c.CustomerID == 9999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AdventureWorksContext_Query_FindsCustomerByFirstName()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "John",
            LastName = "Doe"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Customers
            .Where(c => c.FirstName == "John")
            .FirstOrDefaultAsync();

        // Assert
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("John");
    }

    [Fact]
    public async Task AdventureWorksContext_Query_FiltersByMultipleConditions()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer1 = new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe", CompanyName = "Acme" };
        var customer2 = new Customer { CustomerID = 2, FirstName = "Jane", LastName = "Smith", CompanyName = "TechCorp" };
        context.Customers.AddRange(customer1, customer2);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Customers
            .Where(c => c.FirstName == "John" && c.CompanyName == "Acme")
            .FirstOrDefaultAsync();

        // Assert
        result.Should().NotBeNull();
        result!.CustomerID.Should().Be(1);
    }

    [Fact]
    public async Task AdventureWorksContext_Query_ReturnsAllCustomers()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customers = new[]
        {
            new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe" },
            new Customer { CustomerID = 2, FirstName = "Jane", LastName = "Smith" },
            new Customer { CustomerID = 3, FirstName = "Bob", LastName = "Johnson" }
        };
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Customers.ToListAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task AdventureWorksContext_Query_OrdersByFirstName()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customers = new[]
        {
            new Customer { CustomerID = 1, FirstName = "Zoe", LastName = "Alpha" },
            new Customer { CustomerID = 2, FirstName = "Alice", LastName = "Beta" },
            new Customer { CustomerID = 3, FirstName = "Bob", LastName = "Gamma" }
        };
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Customers
            .OrderBy(c => c.FirstName)
            .ToListAsync();

        // Assert
        result[0].FirstName.Should().Be("Alice");
        result[1].FirstName.Should().Be("Bob");
        result[2].FirstName.Should().Be("Zoe");
    }

    #endregion

    #region CRUD Operations Tests

    [Fact]
    public async Task AdventureWorksContext_Add_PersistsCustomer()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FindAsync(1);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task AdventureWorksContext_AddMultiple_PersistsAllCustomers()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customers = new[]
        {
            new Customer { CustomerID = 1, FirstName = "First", LastName = "User" },
            new Customer { CustomerID = 2, FirstName = "Second", LastName = "User" }
        };

        // Act
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();

        // Assert
        var count = await context.Customers.CountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task AdventureWorksContext_Update_ModifiesExistingCustomer()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Original",
            LastName = "Name"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        customer.FirstName = "Updated";
        context.Customers.Update(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FindAsync(1);
        retrieved!.FirstName.Should().Be("Updated");
    }

    [Fact]
    public async Task AdventureWorksContext_Delete_RemovesCustomer()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer { CustomerID = 1, FirstName = "Delete", LastName = "Me" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        context.Customers.Remove(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FindAsync(1);
        retrieved.Should().BeNull();
    }

    #endregion

    #region AsNoTracking Tests

    [Fact]
    public async Task AdventureWorksContext_AsNoTracking_DoesNotTrackEntities()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "NoTrack",
            LastName = "Test"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        var retrieved = await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerID == 1);

        retrieved!.FirstName = "Modified";
        await context.SaveChangesAsync();

        // Assert
        var finalResult = await context.Customers.FindAsync(1);
        finalResult!.FirstName.Should().Be("NoTrack");
    }

    [Fact]
    public async Task AdventureWorksContext_WithTracking_TracksEntities()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Track",
            LastName = "Test"
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Act
        var retrieved = await context.Customers
            .FirstOrDefaultAsync(c => c.CustomerID == 1);

        retrieved!.FirstName = "Modified";
        await context.SaveChangesAsync();

        // Assert
        var finalResult = await context.Customers.FindAsync(1);
        finalResult!.FirstName.Should().Be("Modified");
    }

    #endregion

    #region Property Mapping Tests

    [Fact]
    public async Task AdventureWorksContext_PropertyMapping_HandlesNullableProperties()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Test",
            LastName = "User",
            CompanyName = null,
            EmailAddress = null,
            Phone = null
        };

        // Act
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FindAsync(1);
        retrieved!.CompanyName.Should().BeNull();
        retrieved.EmailAddress.Should().BeNull();
        retrieved.Phone.Should().BeNull();
    }

    [Fact]
    public async Task AdventureWorksContext_PropertyMapping_PersistsAllProperties()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "John",
            LastName = "Doe",
            CompanyName = "Acme",
            EmailAddress = "john@acme.com",
            Phone = "555-1234"
        };

        // Act
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Assert
        var retrieved = await context.Customers.FindAsync(1);
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("John");
        retrieved.LastName.Should().Be("Doe");
        retrieved.CompanyName.Should().Be("Acme");
        retrieved.EmailAddress.Should().Be("john@acme.com");
        retrieved.Phone.Should().Be("555-1234");
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task AdventureWorksContext_SaveChanges_PreservesDataAcrossInstances()
    {
        // Arrange
        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Persist",
            LastName = "Test"
        };

        // Act
        var context1 = _fixture.GetContext();
        context1.Customers.Add(customer);
        await context1.SaveChangesAsync();
        context1.Dispose();

        var context2 = _fixture.GetContext();
        var retrieved = await context2.Customers.FindAsync(1);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Persist");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task AdventureWorksContext_DuplicateKey_ThrowsException()
    {
        // Arrange
        var context = _fixture.GetContext();
        var customer1 = new Customer { CustomerID = 1, FirstName = "First", LastName = "User" };
        var customer2 = new Customer { CustomerID = 1, FirstName = "Second", LastName = "User" };

        context.Customers.Add(customer1);
        await context.SaveChangesAsync();

        // Act & Assert
        // In-memory database throws InvalidOperationException when adding duplicate key
        Assert.Throws<InvalidOperationException>(() => context.Customers.Add(customer2));
    }

    #endregion

    public void Dispose()
    {
        _fixture?.Dispose();
    }
}
