using System.Linq;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Entities;
using Takeaway.Api.Extensions;
using Takeaway.Api.Services;

namespace Takeaway.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        group.MapPost("", async Task<Results<BadRequest<ValidationProblemDetails>, BadRequest<string>, TooManyRequests, Created<CreateOrderResponse>>>
            (
                CreateOrderRequest request,
                IValidator<CreateOrderRequest> validator,
                TakeawayDbContext dbContext,
                IOrderPricingService pricingService,
                IOrderThrottlingService throttlingService,
                IDateTimeProvider clock,
                IOrderCodeGenerator codeGenerator,
                CancellationToken cancellationToken
            ) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return TypedResults.BadRequest(validationResult.ToProblemDetails());
            }

            var slotStart = NormalizeSlot(request.RequestedSlotUtc ?? clock.UtcNow);
            if (!await throttlingService.CanPlaceOrderAsync(slotStart, cancellationToken))
            {
                return TypedResults.TooManyRequests();
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var order = new Order
            {
                ShopId = request.ShopId,
                OrderChannelId = request.OrderChannelId,
                OrderStatusId = 1,
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
                {
                    return TypedResults.BadRequest("Customer not found");
                }
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
            {
                order.Customer = customer;
            }

            var items = new List<OrderItem>();

            foreach (var item in request.Items)
            {
                var product = await dbContext.Products
                    .Include(p => p.Variants)
                    .Include(p => p.Modifiers)
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);

                if (product is null)
                {
                    return TypedResults.BadRequest($"Product {item.ProductId} not found");
                }

                if (!product.IsAvailable)
                {
                    return TypedResults.BadRequest($"Product {product.Name} is not available");
                }

                var variant = item.VariantId.HasValue
                    ? product.Variants.FirstOrDefault(v => v.Id == item.VariantId.Value)
                    : product.Variants.FirstOrDefault(v => v.IsDefault);

                if (item.VariantId.HasValue && variant is null)
                {
                    return TypedResults.BadRequest($"Variant {item.VariantId.Value} is not available for product {product.Name}");
                }

                var modifiers = item.ModifierIds is { Count: > 0 }
                    ? product.Modifiers.Where(m => item.ModifierIds.Contains(m.Id)).ToList()
                    : new List<ProductModifier>();

                if (item.ModifierIds is { Count: > 0 } && modifiers.Count != item.ModifierIds.Count)
                {
                    return TypedResults.BadRequest("One or more modifiers are invalid");
                }

                var availableStock = variant?.StockQuantity ?? product.StockQuantity;
                if (availableStock < item.Quantity)
                {
                    return TypedResults.BadRequest($"Insufficient stock for {product.Name}");
                }

                var pricing = pricingService.Calculate(product, variant, modifiers, item.Quantity);

                order.TotalAmount += pricing.Subtotal;

                var appliedModifiers = modifiers.Select(m => m.Name).ToArray();

                var orderItem = new OrderItem
                {
                    ProductId = product.Id,
                    ProductVariantId = variant?.Id,
                    VariantName = variant?.Name,
                    Quantity = item.Quantity,
                    UnitPrice = pricing.UnitPrice,
                    Subtotal = pricing.Subtotal,
                    Modifiers = appliedModifiers.Length > 0 ? JsonSerializer.Serialize(appliedModifiers) : null
                };

                items.Add(orderItem);

                if (variant is not null)
                {
                    variant.StockQuantity -= item.Quantity;
                }
                else
                {
                    product.StockQuantity -= item.Quantity;
                }
            }

            if (order.TotalAmount <= 0)
            {
                return TypedResults.BadRequest("Order total must be greater than zero");
            }

            order.Items = items;
            dbContext.Orders.Add(order);

            if (request.PaymentMethodId.HasValue)
            {
                var paymentMethodExists = await dbContext.PaymentMethods.AnyAsync(
                    pm => pm.Id == request.PaymentMethodId.Value,
                    cancellationToken);
                if (!paymentMethodExists)
                {
                    return TypedResults.BadRequest("Payment method not found");
                }

                order.Payments.Add(new Payment
                {
                    PaymentMethodId = request.PaymentMethodId.Value,
                    Amount = order.TotalAmount,
                    PaymentDate = clock.UtcNow,
                    Status = "Pending"
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            order.OrderCode = codeGenerator.Generate(slotStart, order.Id);
            await dbContext.SaveChangesAsync(cancellationToken);

            var auditLog = new AuditLog
            {
                OrderId = order.Id,
                EventType = "OrderCreated",
                CreatedAt = clock.UtcNow,
                Payload = JsonSerializer.Serialize(new
                {
                    order.OrderCode,
                    order.TotalAmount,
                    Items = order.Items.Select(i => new { i.ProductId, i.Quantity, i.ProductVariantId, i.UnitPrice, i.Subtotal })
                })
            };

            dbContext.AuditLogs.Add(auditLog);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new CreateOrderResponse(order.Id, order.OrderCode, order.TotalAmount, order.OrderDate);
            return TypedResults.Created($"/orders/{order.Id}", response);
        })
        .WithName("CreateOrder")
        .WithSummary("Create a new order with pricing, validation and throttling");

        return app;
    }

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
