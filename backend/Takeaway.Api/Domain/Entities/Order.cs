namespace Takeaway.Api.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public int? CustomerId { get; set; }
    public int OrderChannelId { get; set; }
    public int OrderStatusId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public Shop Shop { get; set; } = null!;
    public Customer? Customer { get; set; }
    public OrderChannel OrderChannel { get; set; } = null!;
    public OrderStatus OrderStatus { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
