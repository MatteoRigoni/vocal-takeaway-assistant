using System.Text.Json.Serialization;

namespace Takeaway.Api.Contracts.Orders;

public record OrderStatusResponse(
    [property: JsonPropertyName("orderId")] int OrderId,
    [property: JsonPropertyName("orderCode")] string OrderCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("pickupAt")] DateTime PickupAtUtc,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("isPaid")] bool IsPaid,
    [property: JsonPropertyName("payments")] IReadOnlyCollection<OrderPaymentDto> Payments);

public record OrderPaymentDto(
    [property: JsonPropertyName("paymentId")] int PaymentId,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("paymentDate")] DateTime PaymentDateUtc,
    [property: JsonPropertyName("status")] string Status);
