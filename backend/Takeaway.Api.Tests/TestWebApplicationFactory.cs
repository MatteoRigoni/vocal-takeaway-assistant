using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Takeaway.Api.Data;

namespace Takeaway.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int? _maxOrdersPerSlot;

    public TestWebApplicationFactory(int? maxOrdersPerSlot = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _maxOrdersPerSlot = maxOrdersPerSlot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_maxOrdersPerSlot.HasValue)
        {
            builder.ConfigureAppConfiguration((context, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OrderThrottling:MaxOrdersPerSlot"] = _maxOrdersPerSlot.Value.ToString()
                });
            });
        }

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TakeawayDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TakeawayDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TakeawayDbContext>();
            db.Database.EnsureDeleted();
            db.Database.Migrate();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
