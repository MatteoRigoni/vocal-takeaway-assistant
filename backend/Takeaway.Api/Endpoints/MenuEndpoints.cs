using System.Linq;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Contracts.Menu;
using Takeaway.Api.Data;

namespace Takeaway.Api.Endpoints;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/menu");

        group.MapGet("", async (TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var categories = await dbContext.Categories
                .Include(c => c.Products.Where(p => p.IsAvailable))
                    .ThenInclude(p => p.Variants)
                .Include(c => c.Products.Where(p => p.IsAvailable))
                    .ThenInclude(p => p.Modifiers)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            var response = new MenuResponse(categories
                .Select(category => new MenuCategoryDto(
                    category.Id,
                    category.Name,
                    category.Description,
                    category.Products
                        .OrderBy(p => p.Name)
                        .Select(product => new MenuProductDto(
                            product.Id,
                            product.Name,
                            product.Description,
                            product.Price,
                            product.VatRate,
                            product.IsAvailable,
                            product.StockQuantity,
                            product.Variants
                                .OrderBy(v => v.Name)
                                .Select(v => new MenuProductVariantDto(v.Id, v.Name, v.Price, v.VatRate, v.IsDefault, v.StockQuantity))
                                .ToList(),
                            product.Modifiers
                                .OrderBy(m => m.Name)
                                .Select(m => new MenuProductModifierDto(m.Id, m.Name, m.Price, m.VatRate))
                                .ToList()))
                        .ToList()))
                .ToList());

            return Results.Ok(response);
        })
        .WithName("GetMenu")
        .WithSummary("Retrieve the current shop menu");

        return app;
    }
}
