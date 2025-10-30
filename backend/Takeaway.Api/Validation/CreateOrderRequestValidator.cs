using FluentValidation;
using Takeaway.Api.Contracts.Orders;

namespace Takeaway.Api.Validation;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.ShopId).GreaterThan(0);
        RuleFor(r => r.OrderChannelId).GreaterThan(0);
        RuleFor(r => r.DeliveryAddress).NotEmpty();
        RuleFor(r => r.Items)
            .NotNull()
            .Must(items => items.Count > 0)
            .WithMessage("At least one item is required");

        RuleForEach(r => r.Items).SetValidator(new OrderItemRequestValidator());
    }

    private class OrderItemRequestValidator : AbstractValidator<OrderItemRequest>
    {
        public OrderItemRequestValidator()
        {
            RuleFor(i => i.ProductId).GreaterThan(0);
            RuleFor(i => i.Quantity).GreaterThan(0);
        }
    }
}
