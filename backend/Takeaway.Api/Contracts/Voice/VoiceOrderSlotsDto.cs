using System;
using System.Collections.Generic;

namespace Takeaway.Api.Contracts.Voice;

public sealed record VoiceOrderSlotsDto(
    ProductSlotDto Product,
    VariantSlotDto Variant,
    QuantitySlotDto Quantity,
    ModifiersSlotDto Modifiers,
    PickupTimeSlotDto PickupTime)
{
    public static VoiceOrderSlotsDto Empty { get; } = new(
        new ProductSlotDto(null, null, false),
        new VariantSlotDto(null, null, null, false),
        new QuantitySlotDto(null, false),
        new ModifiersSlotDto(Array.Empty<ModifierSelectionDto>(), false, false),
        new PickupTimeSlotDto(null, false));
}

public sealed record ProductSlotDto(int? ProductId, string? Name, bool IsFilled);

public sealed record VariantSlotDto(int? VariantId, string? Name, int? ProductId, bool IsFilled);

public sealed record QuantitySlotDto(int? Quantity, bool IsFilled);

public sealed record ModifierSelectionDto(int ModifierId, string Name, int ProductId);

public sealed record ModifiersSlotDto(IReadOnlyList<ModifierSelectionDto> Selections, bool IsFilled, bool IsExplicitNone);

public sealed record PickupTimeSlotDto(DateTimeOffset? Value, bool IsFilled);
