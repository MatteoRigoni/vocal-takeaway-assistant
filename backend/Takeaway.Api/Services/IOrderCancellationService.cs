using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Services;

public interface IOrderCancellationService
{
    bool CanCancel(Order order, DateTime utcNow, out string? failureReason);
}
