using Takeaway.Api.Domain.Entities;

namespace Takeaway.Api.Services;

public interface IOrderPricingService
{
    OrderPricingResult Calculate(Product product, ProductVariant? variant, IReadOnlyCollection<ProductModifier> modifiers, int quantity);
}

public sealed class OrderPricingService : IOrderPricingService
{
    public OrderPricingResult Calculate(Product product, ProductVariant? variant, IReadOnlyCollection<ProductModifier> modifiers, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        var basePrice = variant?.Price ?? product.Price;
        var vatRate = variant?.VatRate ?? product.VatRate;
        var basePriceWithVat = ApplyVat(basePrice, vatRate);

        decimal modifiersTotal = 0m;
        foreach (var modifier in modifiers)
        {
            modifiersTotal += ApplyVat(modifier.Price, modifier.VatRate);
        }

        var unitPrice = Math.Round(basePriceWithVat + modifiersTotal, 2, MidpointRounding.AwayFromZero);
        var subtotal = Math.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero);

        return new OrderPricingResult(unitPrice, subtotal, basePriceWithVat, modifiersTotal);
    }

    private static decimal ApplyVat(decimal price, decimal vatRate)
    {
        return Math.Round(price * (1 + vatRate), 4, MidpointRounding.AwayFromZero);
    }
}

public readonly record struct OrderPricingResult(decimal UnitPrice, decimal Subtotal, decimal BasePriceWithVat, decimal ModifiersTotalWithVat);
