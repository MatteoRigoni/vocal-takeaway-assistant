using System;
using System.Collections.Generic;

namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class VoiceOrderSlots
{
    public ProductSlot Product { get; } = new();
    public VariantSlot Variant { get; } = new();
    public QuantitySlot Quantity { get; } = new();
    public ModifiersSlot Modifiers { get; } = new();
    public PickupTimeSlot PickupTime { get; } = new();

    public void ClearAll()
    {
        Product.Clear();
        Variant.Clear();
        Quantity.Clear();
        Modifiers.Clear();
        PickupTime.Clear();
    }

    public void SetProduct(ProductSelection selection)
    {
        var hasChanged = Product.Selection?.ProductId != selection.ProductId;
        Product.Set(selection);
        if (hasChanged)
        {
            Variant.Clear();
            Modifiers.Clear();
        }
    }

    public void ClearProduct()
    {
        Product.Clear();
        Variant.Clear();
        Quantity.Clear();
        Modifiers.Clear();
        PickupTime.Clear();
    }

    public void SetVariant(VariantSelection selection)
    {
        if (Product.Selection is null || Product.Selection.ProductId != selection.ProductId)
        {
            return;
        }

        Variant.Set(selection);
    }

    public void ClearVariant()
    {
        Variant.Clear();
    }

    public void SetQuantity(int quantity)
    {
        Quantity.Set(quantity);
    }

    public void ClearQuantity()
    {
        Quantity.Clear();
    }

    public void SetModifiers(IEnumerable<ModifierSelection> selections)
    {
        if (Product.Selection is null)
        {
            return;
        }

        Modifiers.Set(selections);
    }

    public void MarkNoModifiers()
    {
        if (Product.Selection is null)
        {
            return;
        }

        Modifiers.MarkNone();
    }

    public void ClearModifiers()
    {
        Modifiers.Clear();
    }

    public void SetPickupTime(DateTimeOffset pickupTime)
    {
        PickupTime.Set(pickupTime);
    }

    public void ClearPickupTime()
    {
        PickupTime.Clear();
    }

    public VoiceOrderSlotsSnapshot ToSnapshot()
    {
        return new VoiceOrderSlotsSnapshot(
            Product.ToSnapshot(),
            Variant.ToSnapshot(),
            Quantity.ToSnapshot(),
            Modifiers.ToSnapshot(),
            PickupTime.ToSnapshot());
    }

    public void ApplySnapshot(VoiceOrderSlotsSnapshot snapshot)
    {
        if (snapshot.Product.IsFilled && snapshot.Product.ProductId.HasValue && !string.IsNullOrWhiteSpace(snapshot.Product.Name))
        {
            SetProduct(new ProductSelection(snapshot.Product.ProductId.Value, snapshot.Product.Name!));
        }
        else
        {
            ClearAll();
        }

        if (snapshot.Variant.IsFilled && snapshot.Variant.VariantId.HasValue && snapshot.Variant.ProductId.HasValue && !string.IsNullOrWhiteSpace(snapshot.Variant.Name))
        {
            SetVariant(new VariantSelection(snapshot.Variant.VariantId.Value, snapshot.Variant.Name!, snapshot.Variant.ProductId.Value));
        }
        else
        {
            ClearVariant();
        }

        if (snapshot.Quantity.IsFilled && snapshot.Quantity.Quantity.HasValue)
        {
            SetQuantity(snapshot.Quantity.Quantity.Value);
        }
        else
        {
            ClearQuantity();
        }

        if (snapshot.Modifiers.IsFilled)
        {
            if (snapshot.Modifiers.IsExplicitNone)
            {
                MarkNoModifiers();
            }
            else
            {
                SetModifiers(snapshot.Modifiers.Selections ?? Array.Empty<ModifierSelection>());
            }
        }
        else
        {
            ClearModifiers();
        }

        if (snapshot.PickupTime.IsFilled && snapshot.PickupTime.Value.HasValue)
        {
            SetPickupTime(snapshot.PickupTime.Value.Value);
        }
        else
        {
            ClearPickupTime();
        }
    }
}
