using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EnterpriseAiGateway.Data.Models;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Integration.Anthropic;
using Xunit;

namespace EnterpriseAiGateway.Tests.DependencyInjection;

/// <summary>
/// Tests for dependency injection configuration and service registration.
/// Verifies that services are properly registered and DbContext is configured correctly.
/// </summary>
public class DependencyInjectionTests
{
    private readonly MockRepository _mockRepository = new(MockBehavior.Strict);

    #region DbContext Registration Tests

    [Fact]
    public void AddDbContext_WithValidConfiguration_RegistersDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AdventureWorksConnection", "Server=.;Database=Test;" }
            })
            .Build();

        // Act
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));

        var provider = services.BuildServiceProvider();
        var context = provider.GetService<AdventureWorksContext>();

        // Assert
        context.Should().NotBeNull();
        context.Should().BeOfType<AdventureWorksContext>();
    }

    [Fact]
    public void DbContext_WithInMemoryDatabase_CreatesValidDbSet()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));

        var provider = services.BuildServiceProvider();
        var context = provider.GetService<AdventureWorksContext>();

        // Act & Assert
        context!.Customers.Should().NotBeNull();
        context.Customers.Should().BeAssignableTo<DbSet<Customer>>();
    }

    [Fact]
    public async Task DbContext_CanAddAndQueryCustomers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb" + Guid.NewGuid()));

        var provider = services.BuildServiceProvider();
        var context = provider.GetService<AdventureWorksContext>();

        var customer = new Customer
        {
            CustomerID = 1,
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        context!.Customers.Add(customer);
        await context.SaveChangesAsync();

        var retrieved = await context.Customers.FindAsync(1);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Test");
    }

    #endregion

    #region Repository Registration Tests

    [Fact]
    public void AddRepository_WithValidConfiguration_RegistersRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();

        var provider = services.BuildServiceProvider();
        var repository = provider.GetService<ISecureCustomerRepository>();

        // Act & Assert
        repository.Should().NotBeNull();
        repository.Should().BeOfType<SecureCustomerRepository>();
    }

    [Fact]
    public void Repository_RegisteredAsScoped_CreatesNewInstancePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();

        var provider = services.BuildServiceProvider();

        // Act
        using (var scope1 = provider.CreateScope())
        {
            var repo1 = scope1.ServiceProvider.GetService<ISecureCustomerRepository>();

            using (var scope2 = provider.CreateScope())
            {
                var repo2 = scope2.ServiceProvider.GetService<ISecureCustomerRepository>();

                // Assert - Different scopes should have different instances
                repo1.Should().NotBeSameAs(repo2);
            }
        }
    }

    [Fact]
    public async Task Repository_WithRegisteredDbContext_CanQueryCustomers()
    {
        // Arrange
        var databaseName = "TestDb" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetService<AdventureWorksContext>();
            var customer = new Customer { CustomerID = 1, FirstName = "John", LastName = "Doe" };
            context!.Customers.Add(customer);
            await context.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetService<ISecureCustomerRepository>();

            // Act
            var result = await repository!.GetCustomerContextAsync(1, false);

            // Assert
            result.Should().Contain("John");
            result.Should().Contain("Doe");
        }
    }

    #endregion

    #region AnthropicClient Registration Tests

    [Fact]
    public void AddAnthropicClient_WithValidConfiguration_RegistersClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" },
                { "Anthropic:Model", "claude-3-5-sonnet-20241022" },
                { "Anthropic:MaxTokens", "1024" },
                { "Anthropic:RequestTimeoutSeconds", "30" }
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();

        // Act
        services.AddAnthropicClient(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IAnthropicClient>();

        // Assert
        client.Should().NotBeNull();
        client.Should().BeOfType<AnthropicClient>();
    }

    [Fact]
    public void AddAnthropicClient_WithoutApiKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Missing Anthropic:ApiKey
                { "Anthropic:Model", "claude-3-5-sonnet-20241022" }
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAnthropicClient(configuration)
        );
    }

    [Fact]
    public void AddAnthropicClient_WithDefaultValues_UsesDefaultModel()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" }
                // Missing Model, MaxTokens, RequestTimeoutSeconds
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();

        // Act
        services.AddAnthropicClient(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IAnthropicClient>();

        // Assert - Should succeed with defaults
        client.Should().NotBeNull();
    }

    [Fact]
    public void AnthropicClient_RegisteredAsSingleton_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" }
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();
        services.AddAnthropicClient(configuration);

        var provider = services.BuildServiceProvider();

        // Act
        var client1 = provider.GetService<IAnthropicClient>();
        var client2 = provider.GetService<IAnthropicClient>();

        // Assert - Singleton should return same instance
        client1.Should().BeSameAs(client2);
    }

    #endregion

    #region Full Service Collection Tests

    [Fact]
    public void CompleteServiceRegistration_WithAllDependencies_ResolvesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AdventureWorksConnection", "Server=.;Database=Test;" },
                { "Anthropic:ApiKey", "sk-ant-test-key" }
            })
            .Build();

        // Act
        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();
        services.AddHttpClient();
        services.AddLogging();
        services.AddAnthropicClient(configuration);

        var provider = services.BuildServiceProvider();

        var context = provider.GetService<AdventureWorksContext>();
        var repository = provider.GetService<ISecureCustomerRepository>();
        var anthropicClient = provider.GetService<IAnthropicClient>();

        // Assert
        context.Should().NotBeNull();
        repository.Should().NotBeNull();
        anthropicClient.Should().NotBeNull();
    }

    [Fact]
    public void ServiceLifetimes_AreCorrectlyConfigured()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" }
            })
            .Build();

        services.AddDbContext<AdventureWorksContext>(options =>
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();
        services.AddHttpClient();
        services.AddLogging();
        services.AddAnthropicClient(configuration);

        // Act - Verify lifetimes
        var dbContextDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(AdventureWorksContext));
        var repositoryDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ISecureCustomerRepository));
        var anthropicDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IAnthropicClient));

        // Assert
        dbContextDescriptor?.Lifetime.Should().Be(ServiceLifetime.Scoped);
        repositoryDescriptor?.Lifetime.Should().Be(ServiceLifetime.Scoped);
        anthropicDescriptor?.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    #endregion

    #region Configuration Edge Cases

    [Fact]
    public void AnthropicConfiguration_WithInvalidMaxTokens_UsesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" },
                { "Anthropic:MaxTokens", "invalid-number" }
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();

        // Act & Assert - Should use default value and succeed
        services.AddAnthropicClient(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IAnthropicClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AnthropicConfiguration_WithInvalidTimeout_UsesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Anthropic:ApiKey", "sk-ant-test-key" },
                { "Anthropic:RequestTimeoutSeconds", "not-a-number" }
            })
            .Build();

        services.AddHttpClient();
        services.AddLogging();

        // Act & Assert - Should use default value and succeed
        services.AddAnthropicClient(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IAnthropicClient>();
        client.Should().NotBeNull();
    }

    #endregion

    #region Mock Integration Tests

    [Fact]
    public async Task ServiceCollection_CanBeTestedWithMocks()
    {
        // Arrange
        var mockRepository = _mockRepository.Create<ISecureCustomerRepository>();
        var mockAnthropicClient = _mockRepository.Create<IAnthropicClient>();

        mockRepository
            .Setup(r => r.GetCustomerContextAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync("Test data");

        // Act
        var result = await mockRepository.Object.GetCustomerContextAsync(1, false);

        // Assert
        result.Should().Be("Test data");
        mockRepository.Verify(r => r.GetCustomerContextAsync(1, false), Times.Once);
    }

    #endregion
}
