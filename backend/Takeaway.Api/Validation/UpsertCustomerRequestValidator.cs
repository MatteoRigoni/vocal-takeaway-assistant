using FluentValidation;
using Takeaway.Api.Contracts.Customers;

namespace Takeaway.Api.Validation;

public class UpsertCustomerRequestValidator : AbstractValidator<UpsertCustomerRequest>
{
    public UpsertCustomerRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty();
        RuleFor(r => r.Phone).NotEmpty();
        RuleFor(r => r.Email).EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.Email));
    }
}
