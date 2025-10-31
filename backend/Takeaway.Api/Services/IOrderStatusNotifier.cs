using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Services;

public interface IOrderStatusNotifier
{
    Task NotifyOrderCreatedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyStatusChangedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyPaymentAsync(Order order, Payment payment, CancellationToken cancellationToken = default);
}
