using System;
using System.Linq;
using Takeaway.Api.Contracts.Voice;

namespace Takeaway.Api.VoiceDialog.Slots;

public static class VoiceOrderSlotMapper
{
    public static VoiceOrderSlotsDto ToDto(this VoiceOrderSlotsSnapshot snapshot)
    {
        return new VoiceOrderSlotsDto(
            new ProductSlotDto(snapshot.Product.ProductId, snapshot.Product.Name, snapshot.Product.IsFilled),
            new VariantSlotDto(snapshot.Variant.VariantId, snapshot.Variant.Name, snapshot.Variant.ProductId, snapshot.Variant.IsFilled),
            new QuantitySlotDto(snapshot.Quantity.Quantity, snapshot.Quantity.IsFilled),
            new ModifiersSlotDto(
                snapshot.Modifiers.Selections.Select(m => new ModifierSelectionDto(m.ModifierId, m.Name, m.ProductId)).ToList(),
                snapshot.Modifiers.IsFilled,
                snapshot.Modifiers.IsExplicitNone),
            new PickupTimeSlotDto(snapshot.PickupTime.Value, snapshot.PickupTime.IsFilled));
    }

    public static VoiceOrderSlotsSnapshot ToSnapshot(this VoiceOrderSlotsDto dto)
    {
        return new VoiceOrderSlotsSnapshot(
            new ProductSlotSnapshot(dto.Product.ProductId, dto.Product.Name, dto.Product.IsFilled),
            new VariantSlotSnapshot(dto.Variant.VariantId, dto.Variant.Name, dto.Variant.ProductId, dto.Variant.IsFilled),
            new QuantitySlotSnapshot(dto.Quantity.Quantity, dto.Quantity.IsFilled),
            new ModifiersSlotSnapshot(
                dto.Modifiers.Selections.Select(m => new ModifierSelection(m.ModifierId, m.Name, m.ProductId)).ToList(),
                dto.Modifiers.IsFilled,
                dto.Modifiers.IsExplicitNone),
            new PickupTimeSlotSnapshot(dto.PickupTime.Value, dto.PickupTime.IsFilled));
    }
}
