using System;

namespace Takeaway.Api.VoiceDialog.Slots;

public sealed record VoiceOrderSlotsSnapshot(
    ProductSlotSnapshot Product,
    VariantSlotSnapshot Variant,
    QuantitySlotSnapshot Quantity,
    ModifiersSlotSnapshot Modifiers,
    PickupTimeSlotSnapshot PickupTime)
{
    public static VoiceOrderSlotsSnapshot Empty { get; } = new(
        new ProductSlotSnapshot(null, null, false),
        new VariantSlotSnapshot(null, null, null, false),
        new QuantitySlotSnapshot(null, false),
        new ModifiersSlotSnapshot(Array.Empty<ModifierSelection>(), false, false),
        new PickupTimeSlotSnapshot(null, false));
}
