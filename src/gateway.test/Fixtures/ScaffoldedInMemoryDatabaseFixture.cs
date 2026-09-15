using EnterpriseAiGateway.Data.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAiGateway.Tests.Fixtures;

public sealed class ScaffoldedInMemoryDatabaseFixture : IDisposable
{
    private readonly DbContextOptions<AdventureWorksDbContext> _options;
    private AdventureWorksDbContext? _context;

    public ScaffoldedInMemoryDatabaseFixture()
    {
        _options = new DbContextOptionsBuilder<AdventureWorksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    public AdventureWorksDbContext GetContext()
    {
        _context = new AdventureWorksDbContext(_options);
        return _context;
    }

    public void Dispose() => _context?.Dispose();
}