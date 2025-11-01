namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class QuantitySlot
{
    public int? Value { get; private set; }

    public bool IsFilled => Value.HasValue;

    public void Set(int quantity)
    {
        Value = quantity;
    }

    public void Clear()
    {
        Value = null;
    }

    public QuantitySlotSnapshot ToSnapshot()
    {
        return new QuantitySlotSnapshot(Value, IsFilled);
    }
}

public sealed record QuantitySlotSnapshot(int? Quantity, bool IsFilled);
