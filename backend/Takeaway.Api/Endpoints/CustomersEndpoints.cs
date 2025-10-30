using System.Linq;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Contracts.Customers;
using Takeaway.Api.Data;
using Takeaway.Api.Extensions;

namespace Takeaway.Api.Endpoints;

public static class CustomersEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers");

        group.MapGet("", async (TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var customers = await dbContext.Customers
                .OrderBy(c => c.Name)
                .Select(c => new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Address))
                .ToListAsync(cancellationToken);

            return Results.Ok(customers);
        }).WithName("ListCustomers");

        group.MapGet("/{id:int}", async Task<Results<Ok<CustomerDto>, NotFound>> (int id, TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var customer = await dbContext.Customers
                .Where(c => c.Id == id)
                .Select(c => new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Address))
                .FirstOrDefaultAsync(cancellationToken);

            return customer is null ? TypedResults.NotFound() : TypedResults.Ok(customer);
        }).WithName("GetCustomer");

        group.MapPost("", async Task<Results<Created<CustomerDto>, BadRequest<ValidationProblemDetails>>>
            (UpsertCustomerRequest request, IValidator<UpsertCustomerRequest> validator, TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return TypedResults.BadRequest(validationResult.ToProblemDetails());
            }

            var customer = new Domain.Entities.Customer
            {
                Name = request.Name,
                Phone = request.Phone,
                Email = request.Email ?? string.Empty,
                Address = request.Address
            };

            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync(cancellationToken);

            var dto = new CustomerDto(customer.Id, customer.Name, customer.Phone, customer.Email, customer.Address);
            return TypedResults.Created($"/customers/{customer.Id}", dto);
        }).WithName("CreateCustomer");

        group.MapPut("/{id:int}", async Task<Results<Ok<CustomerDto>, NotFound, BadRequest<ValidationProblemDetails>>>
            (int id, UpsertCustomerRequest request, IValidator<UpsertCustomerRequest> validator, TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return TypedResults.BadRequest(validationResult.ToProblemDetails());
            }

            var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (customer is null)
            {
                return TypedResults.NotFound();
            }

            customer.Name = request.Name;
            customer.Phone = request.Phone;
            customer.Email = request.Email ?? string.Empty;
            customer.Address = request.Address;

            await dbContext.SaveChangesAsync(cancellationToken);

            var dto = new CustomerDto(customer.Id, customer.Name, customer.Phone, customer.Email, customer.Address);
            return TypedResults.Ok(dto);
        }).WithName("UpdateCustomer");

        group.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, TakeawayDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (customer is null)
            {
                return TypedResults.NotFound();
            }

            dbContext.Customers.Remove(customer);
            await dbContext.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        }).WithName("DeleteCustomer");

        return app;
    }
}
