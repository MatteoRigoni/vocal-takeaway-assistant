using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Constants;
using Xunit;

namespace Takeaway.Api.Tests.Integration;

public class OrdersEndpointsTests : IAsyncLifetime
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
    public async Task GetOrderByCode_ReturnsStatus()
    {
        var slot = DateTime.UtcNow.AddHours(1);
        var createResponse = await CreateOrderAsync(CreateSampleOrder(slot));

        var response = await _client.GetAsync($"/orders/{createResponse.OrderCode}");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<OrderStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal(createResponse.OrderId, status!.OrderId);
        Assert.Equal(OrderStatusCatalog.Received, status.Status);
        Assert.Equal(createResponse.TotalAmount, status.TotalAmount);
        Assert.False(status.IsPaid);
        Assert.Empty(status.Payments);
    }

    [Fact]
    public async Task PatchOrder_UpdatesStatusPickupAndNotes()
    {
        var originalSlot = DateTime.UtcNow.AddHours(2);
        var createResponse = await CreateOrderAsync(CreateSampleOrder(originalSlot));

        var newPickup = DateTime.UtcNow.AddHours(3);
        var updateRequest = new UpdateOrderRequest("Ready", newPickup, "Prepare quickly");

        var patchResponse = await _client.PatchAsJsonAsync($"/orders/{createResponse.OrderId}", updateRequest);
        patchResponse.EnsureSuccessStatusCode();

        var updated = await patchResponse.Content.ReadFromJsonAsync<OrderStatusResponse>();
        Assert.NotNull(updated);
        Assert.Equal(OrderStatusCatalog.Ready, updated!.Status);
        Assert.Equal("Prepare quickly", updated.Notes);
        Assert.Equal(NormalizeSlot(newPickup), updated.PickupAtUtc);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TakeawayDbContext>();
        var order = await dbContext.Orders.Include(o => o.OrderStatus).FirstAsync(o => o.Id == createResponse.OrderId);
        Assert.Equal(OrderStatusCatalog.Ready, order.OrderStatus.Name);
        Assert.Equal(NormalizeSlot(newPickup), order.OrderDate);
    }

    [Fact]
    public async Task PatchOrder_AllowsCancellationBeforeWindow()
    {
        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddHours(2)));
        var cancelRequest = new UpdateOrderRequest("Cancelled", null, "Customer request");

        var patchResponse = await _client.PatchAsJsonAsync($"/orders/{createResponse.OrderId}", cancelRequest);
        patchResponse.EnsureSuccessStatusCode();

        var cancelled = await patchResponse.Content.ReadFromJsonAsync<OrderStatusResponse>();
        Assert.NotNull(cancelled);
        Assert.Equal(OrderStatusCatalog.Cancelled, cancelled!.Status);
        Assert.Equal("Customer request", cancelled.Notes);
    }

    [Fact]
    public async Task PatchOrder_RejectsCancellationInsideWindow()
    {
        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddHours(1)));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TakeawayDbContext>();
            var order = await dbContext.Orders.FirstAsync(o => o.Id == createResponse.OrderId);
            order.OrderDate = DateTime.UtcNow.AddMinutes(5);
            await dbContext.SaveChangesAsync();
        }

        var cancelRequest = new UpdateOrderRequest("Cancelled", null, null);
        var patchResponse = await _client.PatchAsJsonAsync($"/orders/{createResponse.OrderId}", cancelRequest);

        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
        var body = await patchResponse.Content.ReadAsStringAsync();
        Assert.Contains("cancelled", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostPay_CreatesCompletedPayment()
    {
        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddHours(3)));

        var payResponse = await _client.PostAsJsonAsync($"/orders/{createResponse.OrderId}/pay", new PayOrderRequest("cash"));
        payResponse.EnsureSuccessStatusCode();

        var status = await payResponse.Content.ReadFromJsonAsync<OrderStatusResponse>();
        Assert.NotNull(status);
        Assert.True(status!.IsPaid);
        var payment = Assert.Single(status.Payments);
        Assert.Equal("Cash", payment.Method);
        Assert.Equal(createResponse.TotalAmount, payment.Amount);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TakeawayDbContext>();
        var order = await dbContext.Orders.Include(o => o.Payments).ThenInclude(p => p.PaymentMethod).FirstAsync(o => o.Id == createResponse.OrderId);
        Assert.Single(order.Payments);
        Assert.Equal("Completed", order.Payments.First().Status);
    }

    [Fact]
    public async Task PostPay_ReturnsBadRequestWhenAlreadyPaid()
    {
        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddHours(3)));

        var first = await _client.PostAsJsonAsync($"/orders/{createResponse.OrderId}/pay", new PayOrderRequest("card"));
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync($"/orders/{createResponse.OrderId}/pay", new PayOrderRequest("card"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("completed payment", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _client.PostAsJsonAsync("/orders", request);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.NotNull(created);
        return created!;
    }

    private static CreateOrderRequest CreateSampleOrder(DateTime? slotUtc)
        => new(
            ShopId: 1,
            OrderChannelId: 1,
            CustomerId: null,
            Customer: new CustomerRequest("Mario Rossi", "+390551234569", "mario@example.com", "Via Roma 10"),
            PaymentMethodId: null,
            DeliveryAddress: "Via Roma 10",
            Notes: "No onions",
            RequestedSlotUtc: slotUtc,
            Items: new[]
            {
                new OrderItemRequest(
                    ProductId: 1,
                    Quantity: 1,
                    VariantId: 1,
                    ModifierIds: Array.Empty<int>())
            });

    private static DateTime NormalizeSlot(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        }

        var slotMinutes = (timestampUtc.Minute / 15) * 15;
        return new DateTime(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day, timestampUtc.Hour, slotMinutes, 0, DateTimeKind.Utc);
    }
}
