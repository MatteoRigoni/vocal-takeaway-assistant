namespace Takeaway.Api.Domain.Entities;

public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal VatRate { get; set; }
    public bool IsDefault { get; set; }
    public int StockQuantity { get; set; }

    public Product Product { get; set; } = null!;
}
