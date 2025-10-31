using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Extensions;

public static class OrderMappingExtensions
{
    public static IQueryable<Order> IncludeKitchenDisplayData(this IQueryable<Order> query)
        => query
            .Include(o => o.OrderStatus)
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product);

    public static KdsOrderTicketDto ToKitchenTicketDto(this Order order)
    {
        var status = order.OrderStatus?.Name ?? OrderStatusCatalog.Received;
        var items = order.Items
            .OrderBy(i => i.Id)
            .Select(i => new KdsOrderItemDto(
                i.Id,
                i.Product?.Name ?? string.Empty,
                i.VariantName,
                ParseModifiers(i.Modifiers),
                i.Quantity))
            .ToList();

        return new KdsOrderTicketDto(
            order.Id,
            order.OrderCode,
            status,
            order.CreatedAt,
            order.OrderDate,
            order.Customer?.Name,
            order.Customer?.Phone,
            order.Notes,
            order.TotalAmount,
            items);
    }

    private static IReadOnlyList<string> ParseModifiers(string? modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(modifiers);
            return parsed ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
