using System.Text.Json.Serialization;

namespace Takeaway.Api.Contracts.Orders;

public record PayOrderRequest([property: JsonPropertyName("method")] string Method);
