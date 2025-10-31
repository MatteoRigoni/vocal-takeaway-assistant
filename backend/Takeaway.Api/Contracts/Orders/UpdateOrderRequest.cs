using System.Text.Json.Serialization;

namespace Takeaway.Api.Contracts.Orders;

public record UpdateOrderRequest(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("pickupAt")] DateTime? PickupAtUtc,
    [property: JsonPropertyName("notes")] string? Notes);
