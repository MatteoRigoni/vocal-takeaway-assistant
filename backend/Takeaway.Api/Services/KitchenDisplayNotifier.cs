using Microsoft.AspNetCore.SignalR;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Extensions;
using Takeaway.Api.Hubs;

namespace Takeaway.Api.Services;

public class KitchenDisplayNotifier : IKitchenDisplayNotifier
{
    private readonly IHubContext<KdsHub> _hubContext;

    public KitchenDisplayNotifier(IHubContext<KdsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyTicketCreatedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dto = order.ToKitchenTicketDto();
        return _hubContext.Clients.All.SendAsync("TicketCreated", dto, cancellationToken);
    }

    public Task NotifyTicketUpdatedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dto = order.ToKitchenTicketDto();
        return _hubContext.Clients.All.SendAsync("TicketUpdated", dto, cancellationToken);
    }

    public Task NotifyTicketRemovedAsync(int orderId, CancellationToken cancellationToken = default)
        => _hubContext.Clients.All.SendAsync("TicketRemoved", new { OrderId = orderId }, cancellationToken);
}
