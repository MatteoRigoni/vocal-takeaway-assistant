namespace Takeaway.Api.Domain.Entities;

public class OrderChannel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
