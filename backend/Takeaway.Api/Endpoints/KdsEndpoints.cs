using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Authorization;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Extensions;

namespace Takeaway.Api.Endpoints;

public static class KdsEndpoints
{
    public static IEndpointRouteBuilder MapKitchenDisplayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/kds")
            .RequireAuthorization(AuthorizationPolicies.ManageKitchen);

        group.MapGet("/tickets", HandleTicketsRequest)
            .WithName("GetKitchenTickets")
            .WithSummary("Retrieve the active kitchen display queue.");

        // Legacy route kept for compatibility with earlier clients.
        app.MapGet("/orders/kds", HandleTicketsRequest)
            .ExcludeFromDescription()
            .RequireAuthorization(AuthorizationPolicies.ManageKitchen);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<KdsOrderTicketDto>>> HandleTicketsRequest(
        [AsParameters] TicketsRequest request,
        TakeawayDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .IncludeKitchenDisplayData();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var requested = request.Status
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
    }

    private sealed record TicketsRequest([property: Microsoft.AspNetCore.Mvc.FromQuery(Name = "status")] string? Status);
}
