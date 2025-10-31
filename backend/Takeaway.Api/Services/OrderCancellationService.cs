using Microsoft.Extensions.Options;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Options;

namespace Takeaway.Api.Services;

public class OrderCancellationService : IOrderCancellationService
{
    private readonly OrderCancellationOptions _options;

    public OrderCancellationService(IOptions<OrderCancellationOptions> options)
    {
        _options = options.Value;
    }

    public bool CanCancel(Order order, DateTime utcNow, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(order);

        failureReason = null;

        if (order.OrderStatus is not null)
        {
            var statusName = order.OrderStatus.Name;
            if (string.Equals(statusName, OrderStatusCatalog.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Order is already cancelled.";
                return false;
            }

            if (string.Equals(statusName, OrderStatusCatalog.Completed, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Completed orders cannot be cancelled.";
                return false;
            }

            if (string.Equals(statusName, OrderStatusCatalog.Ready, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Orders ready for pickup cannot be cancelled.";
                return false;
            }
        }

        var window = TimeSpan.FromMinutes(Math.Max(0, _options.CancellationWindowMinutes));
        var cutoff = order.OrderDate - window;

        if (utcNow > cutoff)
        {
            failureReason = $"Orders can no longer be cancelled within {_options.CancellationWindowMinutes} minutes of pickup.";
            return false;
        }

        return true;
    }
}
