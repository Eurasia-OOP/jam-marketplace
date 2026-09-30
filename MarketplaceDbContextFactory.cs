using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SushiMarketplace.Api.Data;

public sealed class MarketplaceDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Marketplace") ??
            "Host=localhost;Port=5432;Database=marketplace;Username=marketplace;Password=marketplace";
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new MarketplaceDbContext(options);
    }
}
