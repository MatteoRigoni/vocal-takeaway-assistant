namespace Takeaway.Api.Contracts.Orders;

public record CreateOrderRequest(
    int ShopId,
    int OrderChannelId,
    int? CustomerId,
    CustomerRequest? Customer,
    int? PaymentMethodId,
    string DeliveryAddress,
    string? Notes,
    DateTime? RequestedSlotUtc,
    IReadOnlyCollection<OrderItemRequest> Items);

public record CustomerRequest(
    string Name,
    string Phone,
    string? Email,
    string? Address);

public record OrderItemRequest(
    int ProductId,
    int Quantity,
    int? VariantId,
    IReadOnlyCollection<int>? ModifierIds);

public record CreateOrderResponse(int OrderId, string OrderCode, decimal TotalAmount, DateTime OrderDateUtc);
