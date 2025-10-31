namespace Takeaway.Api.Contracts.Orders;

public record KdsOrderTicketDto(
    int OrderId,
    string OrderCode,
    string Status,
    DateTime CreatedAtUtc,
    DateTime PickupAtUtc,
    string? CustomerName,
    string? CustomerPhone,
    string? Notes,
    decimal TotalAmount,
    IReadOnlyList<KdsOrderItemDto> Items);
