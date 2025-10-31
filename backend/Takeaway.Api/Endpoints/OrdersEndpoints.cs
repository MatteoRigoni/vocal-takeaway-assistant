using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Extensions;
using Takeaway.Api.Services;

namespace Takeaway.Api.Endpoints;

public static class OrdersEndpoints
{
    private static readonly Dictionary<string, string> PaymentMethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cash"] = "Cash",
        ["card"] = "CreditCard",
        ["creditcard"] = "CreditCard",
        ["online"] = "Digital",
        ["online-sim"] = "Digital",
        ["online_sim"] = "Digital",
        ["digital"] = "Digital"
    };

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        group.MapGet("/{code}",
            async Task<Results<Ok<OrderStatusResponse>, NotFound>>
            (
                string code,
                TakeawayDbContext dbContext,
                CancellationToken cancellationToken
            ) =>
            {
                var order = await BuildOrderQuery(dbContext)
                    .FirstOrDefaultAsync(o => o.OrderCode == code, cancellationToken);

                return order is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ToOrderStatusResponse(order));
            })
        .WithName("GetOrderByCode")
        .WithSummary("Get the current status for an order using its public code.");

        group.MapGet("/kds",
            async Task<Ok<IReadOnlyList<KdsOrderTicketDto>>>
            (
                [FromQuery(Name = "status")] string? statusFilter,
                TakeawayDbContext dbContext,
                CancellationToken cancellationToken
            ) =>
            {
                var query = dbContext.Orders
                    .AsNoTracking()
                    .IncludeKitchenDisplayData();

                if (!string.IsNullOrWhiteSpace(statusFilter))
                {
                    var requested = statusFilter
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => OrderStatusCatalog.TryNormalize(s, out var normalized) ? normalized : s)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    query = query.Where(o => requested.Contains(o.OrderStatus.Name));
                }
                else
                {
                    query = query.Where(o =>
                        o.OrderStatus.Name != OrderStatusCatalog.Completed &&
                        o.OrderStatus.Name != OrderStatusCatalog.Cancelled);
                }

                var orders = await query
                    .OrderBy(o => o.CreatedAt)
                    .ToListAsync(cancellationToken);

                var tickets = orders
                    .Select(o => o.ToKitchenTicketDto())
                    .ToList();

                return TypedResults.Ok<IReadOnlyList<KdsOrderTicketDto>>(tickets);
            })
        .WithName("GetKitchenDisplayOrders")
        .WithSummary("Get the active kitchen display queue.");

        group.MapPatch("/{id:int}",
            async Task<Results<
                Ok<OrderStatusResponse>,
                BadRequest<ValidationProblemDetails>,
                BadRequest<string>,
                NotFound>>
            (
                int id,
                UpdateOrderRequest request,
                IValidator<UpdateOrderRequest> validator,
                TakeawayDbContext dbContext,
                IDateTimeProvider clock,
                IOrderStatusNotifier statusNotifier,
                IKitchenDisplayNotifier kitchenNotifier,
                IOrderCancellationService cancellationService,
                CancellationToken cancellationToken
            ) =>
            {
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());

                var order = await BuildOrderQuery(dbContext)
                    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

                if (order is null)
                    return TypedResults.NotFound();

                var statusChanged = false;
                var kitchenRelevantChange = false;

                if (request.Status is not null)
                {
                    if (!OrderStatusCatalog.TryNormalize(request.Status, out var normalizedStatus))
                        return TypedResults.BadRequest($"Unknown status '{request.Status}'.");

                    var status = await dbContext.OrderStatuses
                        .FirstOrDefaultAsync(s => s.Name == normalizedStatus, cancellationToken);

                    if (status is null)
                        return TypedResults.BadRequest($"Status '{normalizedStatus}' is not available.");

                    if (string.Equals(normalizedStatus, OrderStatusCatalog.Cancelled, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!cancellationService.CanCancel(order, clock.UtcNow, out var reason))
                            return TypedResults.BadRequest(reason ?? "Order cannot be cancelled.");
                    }

                    if (order.OrderStatusId != status.Id)
                    {
                        var previousStatus = order.OrderStatus?.Name;

                        order.OrderStatusId = status.Id;
                        order.OrderStatus = status;
                        statusChanged = true;
                        kitchenRelevantChange = true;

                        dbContext.AuditLogs.Add(new AuditLog
                        {
                            OrderId = order.Id,
                            EventType = "OrderStatusUpdated",
                            CreatedAt = clock.UtcNow,
                            Payload = JsonSerializer.Serialize(new
                            {
                                Previous = previousStatus,
                                Current = status.Name
                            })
                        });
                    }
                }

                if (request.PickupAtUtc.HasValue)
                {
                    var pickupUtc = NormalizeToUtc(request.PickupAtUtc.Value);
                    order.OrderDate = NormalizeSlot(pickupUtc);
                    kitchenRelevantChange = true;
                }

                if (request.Notes is not null)
                {
                    order.Notes = request.Notes;
                    kitchenRelevantChange = true;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                if (statusChanged)
                {
                    await statusNotifier.NotifyStatusChangedAsync(order, cancellationToken);
                }

                if (kitchenRelevantChange)
                {
                    if (ShouldDisplayOnKitchen(order.OrderStatus?.Name))
                    {
                        await kitchenNotifier.NotifyTicketUpdatedAsync(order, cancellationToken);
                    }
                    else
                    {
                        await kitchenNotifier.NotifyTicketRemovedAsync(order.Id, cancellationToken);
                    }
                }

                return TypedResults.Ok(ToOrderStatusResponse(order));
            })
        .WithName("UpdateOrder")
        .WithSummary("Update an order's status, pickup time or notes.");

        group.MapPost("/{id:int}/pay",
            async Task<Results<
                Ok<OrderStatusResponse>,
                BadRequest<ValidationProblemDetails>,
                BadRequest<string>,
                NotFound>>
            (
                int id,
                PayOrderRequest request,
                IValidator<PayOrderRequest> validator,
                TakeawayDbContext dbContext,
                IDateTimeProvider clock,
                IOrderStatusNotifier statusNotifier,
                CancellationToken cancellationToken
            ) =>
            {
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());

                if (!TryResolvePaymentMethod(request.Method, out var paymentMethodName))
                    return TypedResults.BadRequest("Payment method must be one of: cash, card, online-sim.");

                var order = await BuildOrderQuery(dbContext)
                    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

                if (order is null)
                    return TypedResults.NotFound();

                if (order.Payments.Any(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase)))
                    return TypedResults.BadRequest("Order already has a completed payment.");

                var paymentMethod = await dbContext.PaymentMethods
                    .FirstOrDefaultAsync(pm => pm.Name == paymentMethodName, cancellationToken);

                if (paymentMethod is null)
                    return TypedResults.BadRequest($"Payment method '{paymentMethodName}' is not available.");

                var payment = new Payment
                {
                    PaymentMethodId = paymentMethod.Id,
                    Amount = order.TotalAmount,
                    PaymentDate = clock.UtcNow,
                    Status = "Completed",
                    PaymentMethod = paymentMethod
                };

                order.Payments.Add(payment);

                dbContext.AuditLogs.Add(new AuditLog
                {
                    OrderId = order.Id,
                    EventType = "OrderPaid",
                    CreatedAt = clock.UtcNow,
                    Payload = JsonSerializer.Serialize(new
                    {
                        Method = paymentMethod.Name,
                        payment.Amount
                    })
                });

                await dbContext.SaveChangesAsync(cancellationToken);

                await statusNotifier.NotifyPaymentAsync(order, payment, cancellationToken);

                return TypedResults.Ok(ToOrderStatusResponse(order));
            })
        .WithName("PayOrder")
        .WithSummary("Simulate a payment for the specified order.");

        group.MapPost("",
            async Task<Results<
                BadRequest<ValidationProblemDetails>,
                BadRequest<string>,
                ProblemHttpResult,
                Created<CreateOrderResponse>>>
            (
                CreateOrderRequest request,
                IValidator<CreateOrderRequest> validator,
                TakeawayDbContext dbContext,
                IOrderPricingService pricingService,
                IOrderThrottlingService throttlingService,
                IDateTimeProvider clock,
                IOrderCodeGenerator codeGenerator,
                IOrderStatusNotifier statusNotifier,
                IKitchenDisplayNotifier kitchenNotifier,
                HttpResponse httpResponse,
                CancellationToken cancellationToken
            ) =>
            {
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());

                var slotStart = NormalizeSlot(request.RequestedSlotUtc ?? clock.UtcNow);
                var receivedStatus = await dbContext.OrderStatuses
                    .FirstAsync(s => s.Name == OrderStatusCatalog.Received, cancellationToken);

                if (!await throttlingService.CanPlaceOrderAsync(slotStart, cancellationToken))
                {
                    // opzionale: 60 secondi o calcolato dal tuo servizio
                    httpResponse.Headers.Append("Retry-After", "60");
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Rate limit exceeded",
                        detail: "Too many orders for the selected time slot. Please retry later.");
                }

                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                var order = new Order
                {
                    ShopId = request.ShopId,
                    OrderChannelId = request.OrderChannelId,
                    OrderStatusId = receivedStatus.Id,
                    OrderStatus = receivedStatus,
                    OrderDate = slotStart,
                    CreatedAt = clock.UtcNow,
                    DeliveryAddress = request.DeliveryAddress,
                    Notes = request.Notes,
                    TotalAmount = 0m
                };

                Customer? customer = null;

                if (request.CustomerId.HasValue)
                {
                    customer = await dbContext.Customers
                        .FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, cancellationToken);
                    if (customer == null)
                        return TypedResults.BadRequest("Customer not found");
                }
                else if (request.Customer is not null)
                {
                    customer = new Customer
                    {
                        Name = request.Customer.Name,
                        Phone = request.Customer.Phone,
                        Email = request.Customer.Email ?? string.Empty,
                        Address = request.Customer.Address
                    };
                    dbContext.Customers.Add(customer);
                }

                if (customer is not null)
                    order.Customer = customer;

                var items = new List<OrderItem>();

                foreach (var item in request.Items)
                {
                    var product = await dbContext.Products
                        .Include(p => p.Variants)
                        .Include(p => p.Modifiers)
                        .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);

                    if (product is null)
                        return TypedResults.BadRequest($"Product {item.ProductId} not found");

                    if (!product.IsAvailable)
                        return TypedResults.BadRequest($"Product {product.Name} is not available");

                    var variant = item.VariantId.HasValue
                        ? product.Variants.FirstOrDefault(v => v.Id == item.VariantId.Value)
                        : product.Variants.FirstOrDefault(v => v.IsDefault);

                    if (item.VariantId.HasValue && variant is null)
                        return TypedResults.BadRequest($"Variant {item.VariantId.Value} is not available for product {product.Name}");

                    var modifiers = item.ModifierIds is { Count: > 0 }
                        ? product.Modifiers.Where(m => item.ModifierIds.Contains(m.Id)).ToList()
                        : new List<ProductModifier>();

                    if (item.ModifierIds is { Count: > 0 } && modifiers.Count != item.ModifierIds.Count)
                        return TypedResults.BadRequest("One or more modifiers are invalid");

                    var availableStock = variant?.StockQuantity ?? product.StockQuantity;
                    if (availableStock < item.Quantity)
                        return TypedResults.BadRequest($"Insufficient stock for {product.Name}");

                    var pricing = pricingService.Calculate(product, variant, modifiers, item.Quantity);
                    order.TotalAmount += pricing.Subtotal;

                    var appliedModifiers = modifiers.Select(m => m.Name).ToArray();

                    items.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        ProductVariantId = variant?.Id,
                        VariantName = variant?.Name,
                        Quantity = item.Quantity,
                        UnitPrice = pricing.UnitPrice,
                        Subtotal = pricing.Subtotal,
                        Modifiers = appliedModifiers.Length > 0 ? JsonSerializer.Serialize(appliedModifiers) : null,
                        Product = product
                    });

                    if (variant is not null)
                        variant.StockQuantity -= item.Quantity;
                    else
                        product.StockQuantity -= item.Quantity;
                }

                if (order.TotalAmount <= 0)
                    return TypedResults.BadRequest("Order total must be greater than zero");

                order.Items = items;
                dbContext.Orders.Add(order);

                if (request.PaymentMethodId.HasValue)
                {
                    var paymentMethodExists = await dbContext.PaymentMethods
                        .AnyAsync(pm => pm.Id == request.PaymentMethodId.Value, cancellationToken);

                    if (!paymentMethodExists)
                        return TypedResults.BadRequest("Payment method not found");

                    order.Payments.Add(new Payment
                    {
                        PaymentMethodId = request.PaymentMethodId.Value,
                        Amount = order.TotalAmount,
                        PaymentDate = clock.UtcNow,
                        Status = "Pending"
                    });
                }

                // 1° save: serve l'Id per generare il codice
                await dbContext.SaveChangesAsync(cancellationToken);

                order.OrderCode = codeGenerator.Generate(slotStart, order.Id);

                // Audit (lo creiamo dopo aver impostato OrderCode)
                dbContext.AuditLogs.Add(new AuditLog
                {
                    OrderId = order.Id,
                    EventType = "OrderCreated",
                    CreatedAt = clock.UtcNow,
                    Payload = JsonSerializer.Serialize(new
                    {
                        order.OrderCode,
                        order.TotalAmount,
                        Items = order.Items.Select(i => new
                        {
                            i.ProductId,
                            i.Quantity,
                            i.ProductVariantId,
                            i.UnitPrice,
                            i.Subtotal
                        })
                    })
                });

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var response = new CreateOrderResponse(order.Id, order.OrderCode, order.TotalAmount, order.OrderDate);

                await statusNotifier.NotifyOrderCreatedAsync(order, cancellationToken);
                await kitchenNotifier.NotifyTicketCreatedAsync(order, cancellationToken);

                return TypedResults.Created($"/orders/{order.Id}", response);
            })
        .WithName("CreateOrder")
        .WithSummary("Create a new order with pricing, validation and throttling")
        .ProducesProblem(StatusCodes.Status429TooManyRequests); 

        return app;
    }

    private static IQueryable<Order> BuildOrderQuery(TakeawayDbContext dbContext)
        => dbContext.Orders
            .Include(o => o.OrderStatus)
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.Payments)
                .ThenInclude(p => p.PaymentMethod);

    private static OrderStatusResponse ToOrderStatusResponse(Order order)
    {
        var payments = order.Payments
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => new OrderPaymentDto(
                p.Id,
                p.PaymentMethod?.Name ?? string.Empty,
                p.Amount,
                p.PaymentDate,
                p.Status))
            .ToList();

        var isPaid = payments.Any(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase));

        return new OrderStatusResponse(
            order.Id,
            order.OrderCode,
            order.OrderStatus?.Name ?? OrderStatusCatalog.Received,
            order.CreatedAt,
            order.OrderDate,
            order.Notes,
            order.TotalAmount,
            isPaid,
            payments);
    }

    private static bool TryResolvePaymentMethod(string method, out string paymentMethodName)
    {
        paymentMethodName = string.Empty;

        if (string.IsNullOrWhiteSpace(method))
            return false;

        var candidate = method.Trim();

        if (PaymentMethodMap.TryGetValue(candidate, out paymentMethodName))
            return true;

        var normalized = candidate.Replace("_", "-", StringComparison.Ordinal);
        return PaymentMethodMap.TryGetValue(normalized, out paymentMethodName);
    }

    private static DateTime NormalizeToUtc(DateTime timestamp)
        => timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

    private static DateTime NormalizeSlot(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        }

        var slotMinutes = (timestampUtc.Minute / 15) * 15;
        return new DateTime(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day, timestampUtc.Hour, slotMinutes, 0, DateTimeKind.Utc);
    }

    private static bool ShouldDisplayOnKitchen(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        return !string.Equals(status, OrderStatusCatalog.Completed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, OrderStatusCatalog.Cancelled, StringComparison.OrdinalIgnoreCase);
    }
}