using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using Takeaway.Api.Contracts.Orders;

namespace Takeaway.Api.Tests.Integration;

public class OrderThrottlingTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory = new(maxOrdersPerSlot: 2);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await _client.AuthenticateAsAsync(_factory, "admin", "Admin123!");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await Task.CompletedTask;
        _factory.Dispose();
    }

    [Fact]
    public async Task PostOrders_RespectThrottlingLimit()
    {
        var slot = new DateTime(2025, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var baseRequest = new CreateOrderRequest(
            ShopId: 1,
            OrderChannelId: 1,
            CustomerId: null,
            Customer: new CustomerRequest("Giulia", "+390551234568", "giulia@example.com", "Via Milano 5"),
            PaymentMethodId: null,
            DeliveryAddress: "Via Milano 5",
            Notes: null,
            RequestedSlotUtc: slot,
            Items: new[] { new OrderItemRequest(1, 1, 1, Array.Empty<int>()) });

        var first = await _client.PostAsJsonAsync("/orders", baseRequest);
        Assert.True(first.IsSuccessStatusCode);

        var second = await _client.PostAsJsonAsync("/orders", baseRequest);
        Assert.True(second.IsSuccessStatusCode);

        var third = await _client.PostAsJsonAsync("/orders", baseRequest);
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
