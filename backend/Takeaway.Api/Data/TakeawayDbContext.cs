using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Data;

public class TakeawayDbContext : DbContext
{
    public TakeawayDbContext(DbContextOptions<TakeawayDbContext> options) : base(options)
    {
    }

    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderChannel> OrderChannels => Set<OrderChannel>();
    public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(oi => oi.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasOne(p => p.Shop)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.ShopId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        Seed(modelBuilder);
    }

    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shop>().HasData(new Shop
        {
            Id = 1,
            Name = "Vocal Takeaway",
            Address = "123 Voice Lane",
            Phone = "+39 055 1234567",
            Email = "contact@vocaltakeaway.example",
            OpeningHours = "Mon-Sun 11:00-23:00",
            Description = "Demo shop for the voice assistant"
        });

        modelBuilder.Entity<Category>().HasData(
            new Category
            {
                Id = 1,
                Name = "Pizza",
                Description = "Traditional Italian pizzas"
            },
            new Category
            {
                Id = 2,
                Name = "Beverage",
                Description = "Soft drinks and beverages"
            });

        modelBuilder.Entity<Product>().HasData(
            new Product
            {
                Id = 1,
                ShopId = 1,
                CategoryId = 1,
                Name = "Margherita",
                Description = "Classic pizza with tomato, mozzarella and basil",
                Price = 7.50m,
                IsAvailable = true,
                ImageUrl = null
            },
            new Product
            {
                Id = 2,
                ShopId = 1,
                CategoryId = 1,
                Name = "Diavola",
                Description = "Spicy salami pizza with mozzarella",
                Price = 8.50m,
                IsAvailable = true,
                ImageUrl = null
            },
            new Product
            {
                Id = 3,
                ShopId = 1,
                CategoryId = 2,
                Name = "Coca-Cola",
                Description = "Chilled 33cl can",
                Price = 2.50m,
                IsAvailable = true,
                ImageUrl = null
            });

        modelBuilder.Entity<OrderChannel>().HasData(
            new OrderChannel { Id = 1, Name = "Voice", Description = "Voice assistant" },
            new OrderChannel { Id = 2, Name = "Phone", Description = "Phone call" },
            new OrderChannel { Id = 3, Name = "App", Description = "Mobile application" }
        );

        modelBuilder.Entity<OrderStatus>().HasData(
            new OrderStatus { Id = 1, Name = "Received", Description = "Order received" },
            new OrderStatus { Id = 2, Name = "InPreparation", Description = "Order is being prepared" },
            new OrderStatus { Id = 3, Name = "Completed", Description = "Order completed" },
            new OrderStatus { Id = 4, Name = "Cancelled", Description = "Order cancelled" }
        );

        modelBuilder.Entity<PaymentMethod>().HasData(
            new PaymentMethod { Id = 1, Name = "Cash", Description = "Cash payment" },
            new PaymentMethod { Id = 2, Name = "CreditCard", Description = "Credit card payment" },
            new PaymentMethod { Id = 3, Name = "Digital", Description = "Digital wallet" }
        );
    }
}
