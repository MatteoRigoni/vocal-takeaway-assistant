using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Data;
using Xunit;

namespace Takeaway.Api.Tests.Integration;

public class OrderPricingTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await Task.CompletedTask;
        _factory.Dispose();
    }

    [Fact]
    public async Task PostOrders_CalculatesTotalsWithVariantsModifiersAndVat()
    {
        var slot = new DateTime(2025, 1, 1, 18, 0, 0, DateTimeKind.Utc);
        var request = new CreateOrderRequest(
            ShopId: 1,
            OrderChannelId: 1,
            CustomerId: null,
            Customer: new CustomerRequest("Luca P", "+390551234567", "luca@example.com", "Via Roma 10"),
            PaymentMethodId: 1,
            DeliveryAddress: "Via Roma 10",
            Notes: "Extra napkins",
            RequestedSlotUtc: slot,
            Items: new[]
            {
                new OrderItemRequest(
                    ProductId: 1,
                    Quantity: 2,
                    VariantId: 2,
                    ModifierIds: new[] { 1, 2 })
            });

        var response = await _client.PostAsJsonAsync("/orders", request);
        response.EnsureSuccessStatusCode();

        var orderResponse = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.NotNull(orderResponse);
        Assert.Equal(25.30m, orderResponse!.TotalAmount);
        Assert.Equal(slot, orderResponse.OrderDateUtc);
        Assert.False(string.IsNullOrWhiteSpace(orderResponse.OrderCode));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TakeawayDbContext>();
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.AuditLogs)
            .FirstAsync(o => o.Id == orderResponse.OrderId);

        var item = Assert.Single(order.Items);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(2, item.ProductVariantId);
        Assert.Equal(12.65m, item.UnitPrice);
        Assert.Equal(25.30m, item.Subtotal);
        Assert.NotNull(item.Modifiers);

        var audit = Assert.Single(order.AuditLogs);
        Assert.Equal("OrderCreated", audit.EventType);
    }
}
