using Microsoft.EntityFrameworkCore;
using SushiMarketplace.Api.Domain;

namespace SushiMarketplace.Api.Data;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().Property(x => x.Price).HasPrecision(10, 2);
        modelBuilder.Entity<Product>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Order>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Order>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
        modelBuilder.Entity<Order>().HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OrderId);
        modelBuilder.Entity<OrderLine>().Property(x => x.UnitPrice).HasPrecision(10, 2);
        modelBuilder.Entity<OrderEvent>().Property(x => x.FromStatus).HasConversion<string>();
        modelBuilder.Entity<OrderEvent>().Property(x => x.ToStatus).HasConversion<string>();
        modelBuilder.Entity<OrderEvent>().HasIndex(x => new { x.OrderId, x.IdempotencyKey }).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        modelBuilder.Entity<OrderEvent>().HasIndex(x => new { x.OrderId, x.Id });
    }
}
