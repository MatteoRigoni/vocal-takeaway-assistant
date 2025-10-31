using Microsoft.AspNetCore.SignalR;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Hubs;

namespace Takeaway.Api.Services;

public class OrderStatusNotifier : IOrderStatusNotifier
{
    private readonly IHubContext<OrdersHub> _hubContext;

    public OrderStatusNotifier(IHubContext<OrdersHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyOrderCreatedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync("OrderCreated", CreateSnapshot(order), cancellationToken);
    }

    public Task NotifyStatusChangedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync("OrderStatusUpdated", CreateSnapshot(order), cancellationToken);
    }

    public Task NotifyPaymentAsync(Order order, Payment payment, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            order.Id,
            order.OrderCode,
            Payment = new
            {
                payment.Id,
                Method = payment.PaymentMethod?.Name,
                payment.Amount,
                payment.PaymentDate,
                payment.Status
            }
        };

        return _hubContext.Clients.All.SendAsync("OrderPaymentReceived", payload, cancellationToken);
    }

    private static object CreateSnapshot(Order order)
    {
        var status = order.OrderStatus?.Name ?? OrderStatusCatalog.Received;

        return new
        {
            order.Id,
            order.OrderCode,
            Status = status,
            order.CreatedAt,
            PickupAtUtc = order.OrderDate,
            order.Notes
        };
    }
}
