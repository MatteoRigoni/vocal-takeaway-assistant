using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Data;
using Xunit;

namespace Takeaway.Api.Tests;

public class SeedDataTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TakeawayDbContext _dbContext;

    public SeedDataTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TakeawayDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TakeawayDbContext(options);
        _dbContext.Database.EnsureDeleted();
        _dbContext.Database.Migrate();
    }

    [Fact]
    public async Task SeededProducts_AreAvailable()
    {
        var products = await _dbContext.Products
            .OrderBy(p => p.Id)
            .ToListAsync();

        Assert.Equal(3, products.Count);
        Assert.Collection(products,
            p => Assert.Equal("Margherita", p.Name),
            p => Assert.Equal("Diavola", p.Name),
            p => Assert.Equal("Coca-Cola", p.Name));
    }

    [Fact]
    public async Task SeededProducts_IncludeCategoryAndShop()
    {
        var product = await _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Shop)
            .FirstAsync(p => p.Name == "Margherita");

        Assert.Equal("Pizza", product.Category.Name);
        Assert.Equal("Vocal Takeaway", product.Shop.Name);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
