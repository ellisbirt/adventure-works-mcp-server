using Microsoft.EntityFrameworkCore;
using EnterpriseAiGateway.Data.Models;

namespace EnterpriseAiGateway.Tests.Fixtures;

/// <summary>
/// Test fixture for in-memory EF Core database context.
/// Provides a clean database instance for each test.
/// </summary>
public class InMemoryDatabaseFixture : IDisposable
{
    private readonly DbContextOptions<AdventureWorksContext> _options;
    private AdventureWorksContext? _context;

    public InMemoryDatabaseFixture()
    {
        _options = new DbContextOptionsBuilder<AdventureWorksContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public AdventureWorksContext GetContext()
    {
        _context = new AdventureWorksContext(_options);
        return _context;
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
