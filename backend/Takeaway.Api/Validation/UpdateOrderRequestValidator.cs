using FluentValidation;
using Takeaway.Api.Contracts.Orders;
using Takeaway.Api.Domain.Constants;

namespace Takeaway.Api.Validation;

public class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(r => r)
            .Must(r => r.Status is not null || r.PickupAtUtc.HasValue || r.Notes is not null)
            .WithMessage("At least one field must be provided.");

        When(r => r.Status is not null, () =>
        {
            RuleFor(r => r.Status!)
                .Must(status => OrderStatusCatalog.TryNormalize(status, out _))
                .WithMessage(r => $"Unknown status '{r.Status}'.");
        });
    }
}
