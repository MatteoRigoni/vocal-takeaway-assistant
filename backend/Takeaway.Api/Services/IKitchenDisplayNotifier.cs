using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Services;

public interface IKitchenDisplayNotifier
{
    Task NotifyTicketCreatedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyTicketUpdatedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyTicketRemovedAsync(int orderId, CancellationToken cancellationToken = default);
}
