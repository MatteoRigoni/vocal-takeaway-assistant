namespace Takeaway.Api.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal VatRate { get; set; }
    public bool IsAvailable { get; set; }
    public string? ImageUrl { get; set; }
    public int StockQuantity { get; set; }

    public Shop Shop { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductModifier> Modifiers { get; set; } = new List<ProductModifier>();
}
