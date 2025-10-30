namespace Takeaway.Api.Contracts.Customers;

public record CustomerDto(int Id, string Name, string Phone, string? Email, string? Address);

public record UpsertCustomerRequest(string Name, string Phone, string? Email, string? Address);
