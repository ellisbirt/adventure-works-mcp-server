using Microsoft.EntityFrameworkCore;

namespace EnterpriseAiGateway.Data.Models;

public class AdventureWorksContext : DbContext
{
    public AdventureWorksContext(DbContextOptions<AdventureWorksContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Customer table mapping
        modelBuilder.Entity<Customer>()
            .ToTable("Customer", "SalesLT")
            .HasKey(c => c.CustomerID);

        modelBuilder.Entity<Customer>()
            .Property(c => c.CustomerID)
            .HasColumnName("CustomerID");

        modelBuilder.Entity<Customer>()
            .Property(c => c.FirstName)
            .HasColumnName("FirstName")
            .IsRequired();

        modelBuilder.Entity<Customer>()
            .Property(c => c.LastName)
            .HasColumnName("LastName")
            .IsRequired();

        modelBuilder.Entity<Customer>()
            .Property(c => c.CompanyName)
            .HasColumnName("CompanyName");

        modelBuilder.Entity<Customer>()
            .Property(c => c.EmailAddress)
            .HasColumnName("EmailAddress");

        modelBuilder.Entity<Customer>()
            .Property(c => c.Phone)
            .HasColumnName("Phone");
    }
}
