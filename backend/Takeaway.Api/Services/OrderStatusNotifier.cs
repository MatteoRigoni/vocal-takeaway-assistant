using Microsoft.AspNetCore.SignalR;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Hubs;

namespace Takeaway.Api.Services;

public class OrderStatusNotifier : IOrderStatusNotifier
{
    private readonly IHubContext<OrderStatusHub> _hubContext;

    public OrderStatusNotifier(IHubContext<OrderStatusHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyStatusChangedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            order.Id,
            order.OrderCode,
            Status = order.OrderStatus?.Name,
            PickupAtUtc = order.OrderDate,
            order.Notes
        };

        return _hubContext.Clients.All.SendAsync("OrderStatusUpdated", payload, cancellationToken);
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
}
