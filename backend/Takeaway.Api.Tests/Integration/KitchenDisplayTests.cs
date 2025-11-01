using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Domain.Constants;
using Xunit;

namespace Takeaway.Api.Tests.Integration;

public class KitchenDisplayTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory = new();
    private HttpClient _client = null!;
    private HubConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri("http://localhost/hubs/kds"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect()
            .Build();

        await _connection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _client.Dispose();
        _factory.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateOrder_BroadcastsTicketWithinOneSecond()
    {
        var completion = new TaskCompletionSource<KdsOrderTicketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _connection.On<KdsOrderTicketDto>("TicketCreated", ticket => completion.TrySetResult(ticket));

        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddMinutes(30)));

        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(completion.Task, finished);

        var ticket = await completion.Task;
        Assert.Equal(createResponse.OrderId, ticket.OrderId);
        Assert.Equal(OrderStatusCatalog.Received, ticket.Status);
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, ticket.PickupAtUtc.Kind);
    }

    [Fact]
    public async Task UpdateOrderStatus_NotifiesKitchenBoard()
    {
        var order = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddMinutes(20)));

        var updateCompletion = new TaskCompletionSource<KdsOrderTicketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var updateSubscription = _connection.On<KdsOrderTicketDto>("TicketUpdated", dto =>
        {
            if (dto.OrderId == order.OrderId)
            {
                updateCompletion.TrySetResult(dto);
            }
        });

        var patchResponse = await _client.PatchAsJsonAsync($"/orders/{order.OrderId}", new { Status = OrderStatusCatalog.InPreparation });
        patchResponse.EnsureSuccessStatusCode();

        var updateFinished = await Task.WhenAny(updateCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(updateCompletion.Task, updateFinished);
        var updatedTicket = await updateCompletion.Task;
        Assert.Equal(OrderStatusCatalog.InPreparation, updatedTicket.Status);

        var removalCompletion = new TaskCompletionSource<TicketRemovedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var removalSubscription = _connection.On<TicketRemovedPayload>("TicketRemoved", payload =>
        {
            if (payload.OrderId == order.OrderId)
            {
                removalCompletion.TrySetResult(payload);
            }
        });

        var completeResponse = await _client.PatchAsJsonAsync($"/orders/{order.OrderId}", new { Status = OrderStatusCatalog.Completed });
        completeResponse.EnsureSuccessStatusCode();

        var removalFinished = await Task.WhenAny(removalCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(removalCompletion.Task, removalFinished);
        var removed = await removalCompletion.Task;
        Assert.Equal(order.OrderId, removed.OrderId);
    }

    [Fact]
    public async Task UpdateOrderStatus_NotifiesKitchenBoard()
    {
        var order = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddMinutes(20)));

        var updateCompletion = new TaskCompletionSource<KdsOrderTicketDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var updateSubscription = _connection.On<KdsOrderTicketDto>("TicketUpdated", dto =>
        {
            if (dto.OrderId == order.OrderId)
            {
                updateCompletion.TrySetResult(dto);
            }
        });

        var patchResponse = await _client.PatchAsJsonAsync($"/orders/{order.OrderId}", new { Status = OrderStatusCatalog.InPreparation });
        patchResponse.EnsureSuccessStatusCode();

        var updateFinished = await Task.WhenAny(updateCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(updateCompletion.Task, updateFinished);
        var updatedTicket = await updateCompletion.Task;
        Assert.Equal(OrderStatusCatalog.InPreparation, updatedTicket.Status);

        var removalCompletion = new TaskCompletionSource<TicketRemovedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var removalSubscription = _connection.On<TicketRemovedPayload>("TicketRemoved", payload =>
        {
            if (payload.OrderId == order.OrderId)
            {
                removalCompletion.TrySetResult(payload);
            }
        });

        var completeResponse = await _client.PatchAsJsonAsync($"/orders/{order.OrderId}", new { Status = OrderStatusCatalog.Completed });
        completeResponse.EnsureSuccessStatusCode();

        var removalFinished = await Task.WhenAny(removalCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(removalCompletion.Task, removalFinished);
        var removed = await removalCompletion.Task;
        Assert.Equal(order.OrderId, removed.OrderId);
    }

    [Fact]
    public async Task KdsEndpoint_ReturnsActiveTickets()
    {
        var createResponse = await CreateOrderAsync(CreateSampleOrder(DateTime.UtcNow.AddMinutes(45)));

        var tickets = await _client.GetFromJsonAsync<IReadOnlyList<KdsOrderTicketDto>>("/orders/kds");
        Assert.NotNull(tickets);
        var ticket = Assert.Single(tickets!);
        Assert.Equal(createResponse.OrderId, ticket.OrderId);
        Assert.Equal(OrderStatusCatalog.Received, ticket.Status);
        Assert.NotEmpty(ticket.Items);
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, ticket.PickupAtUtc.Kind);
    }

    private async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        var response = await _client.PostAsJsonAsync("/orders", request);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.NotNull(created);
        return created!;
    }

    private static CreateOrderRequest CreateSampleOrder(DateTime? slotUtc) => new(
        ShopId: 1,
        OrderChannelId: 1,
        CustomerId: null,
        Customer: new CustomerRequest("Giulia Bianchi", "+390551234568", "giulia@example.com", "Via Milano 20"),
        PaymentMethodId: null,
        DeliveryAddress: "Via Milano 20",
        Notes: "Extra napkins",
        RequestedSlotUtc: slotUtc,
        Items: new[]
        {
            new OrderItemRequest(
                ProductId: 1,
                Quantity: 1,
                VariantId: 1,
                ModifierIds: Array.Empty<int>())
        });
}

public sealed record TicketRemovedPayload(int OrderId);
