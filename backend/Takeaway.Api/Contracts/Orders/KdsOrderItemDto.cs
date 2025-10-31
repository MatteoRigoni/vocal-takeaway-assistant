namespace Takeaway.Api.Contracts.Orders;

public record KdsOrderItemDto(
    int Id,
    string ProductName,
    string? VariantName,
    IReadOnlyList<string> Modifiers,
    int Quantity);
