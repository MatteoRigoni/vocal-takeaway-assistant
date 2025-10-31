namespace Takeaway.Api.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Payload { get; set; } = string.Empty;

    public Order Order { get; set; } = null!;
}
