using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Takeaway.Api.Data;
using Takeaway.Api.Options;

namespace Takeaway.Api.Services;

public interface IOrderThrottlingService
{
    Task<bool> CanPlaceOrderAsync(DateTime slotStartUtc, CancellationToken cancellationToken = default);
}

public sealed class OrderThrottlingService : IOrderThrottlingService
{
    private readonly TakeawayDbContext _dbContext;
    private readonly OrderThrottlingOptions _options;

    public OrderThrottlingService(TakeawayDbContext dbContext, IOptions<OrderThrottlingOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<bool> CanPlaceOrderAsync(DateTime slotStartUtc, CancellationToken cancellationToken = default)
    {
        var slotEnd = slotStartUtc.AddMinutes(15);
        var ordersInSlot = await _dbContext.Orders
            .CountAsync(o => o.OrderDate >= slotStartUtc && o.OrderDate < slotEnd, cancellationToken);

        return ordersInSlot < _options.MaxOrdersPerSlot;
    }
}
