using System;
using Microsoft.Extensions.Options;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Options;
using Takeaway.Api.Services;
using Xunit;

namespace Takeaway.Api.Tests.Unit;

public class OrderCancellationServiceTests
{
    private readonly OrderCancellationService _service = new(Options.Create(new OrderCancellationOptions
    {
        CancellationWindowMinutes = 10
    }));

    [Theory]
    [InlineData(OrderStatusCatalog.Cancelled)]
    [InlineData(OrderStatusCatalog.Completed)]
    [InlineData(OrderStatusCatalog.Ready)]
    public void CanCancel_ReturnsFalse_ForTerminalStatuses(string status)
    {
        var order = new Order
        {
            OrderDate = DateTime.UtcNow.AddHours(1),
            OrderStatus = new OrderStatus { Name = status }
        };

        var result = _service.CanCancel(order, DateTime.UtcNow, out var reason);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void CanCancel_ReturnsFalse_WhenInsideWindow()
    {
        var service = new OrderCancellationService(Options.Create(new OrderCancellationOptions
        {
            CancellationWindowMinutes = 15
        }));

        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderDate = now.AddMinutes(10),
            OrderStatus = new OrderStatus { Name = OrderStatusCatalog.Received }
        };

        var result = service.CanCancel(order, now, out var reason);

        Assert.False(result);
        Assert.Contains("cancel", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanCancel_ReturnsTrue_WhenBeforeWindow()
    {
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderDate = now.AddHours(1),
            OrderStatus = new OrderStatus { Name = OrderStatusCatalog.InPreparation }
        };

        var result = _service.CanCancel(order, now, out var reason);

        Assert.True(result);
        Assert.Null(reason);
    }
}
