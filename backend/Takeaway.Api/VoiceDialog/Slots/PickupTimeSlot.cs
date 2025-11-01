using System;

namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class PickupTimeSlot
{
    public DateTimeOffset? Value { get; private set; }

    public bool IsFilled => Value.HasValue;

    public void Set(DateTimeOffset pickupTime)
    {
        Value = pickupTime;
    }

    public void Clear()
    {
        Value = null;
    }

    public PickupTimeSlotSnapshot ToSnapshot()
    {
        return new PickupTimeSlotSnapshot(Value, IsFilled);
    }
}

public sealed record PickupTimeSlotSnapshot(DateTimeOffset? Value, bool IsFilled);
