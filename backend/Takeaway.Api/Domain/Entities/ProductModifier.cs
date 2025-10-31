namespace Takeaway.Api.Domain.Entities;

public class ProductModifier
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal VatRate { get; set; }

    public Product Product { get; set; } = null!;
}
