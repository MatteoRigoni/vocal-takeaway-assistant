using FluentValidation;
using Takeaway.Api.Contracts.Orders;

namespace Takeaway.Api.Validation;

public class PayOrderRequestValidator : AbstractValidator<PayOrderRequest>
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash",
        "card",
        "creditcard",
        "online-sim",
        "online_sim",
        "online",
        "digital"
    };

    public PayOrderRequestValidator()
    {
        RuleFor(r => r.Method)
            .NotEmpty()
            .Must(method => AllowedMethods.Contains((method ?? string.Empty).Trim()))
            .WithMessage("Payment method must be one of: cash, card, online-sim.");
    }
}
