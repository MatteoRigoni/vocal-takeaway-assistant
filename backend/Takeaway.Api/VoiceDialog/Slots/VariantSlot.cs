namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class VariantSlot
{
    public VariantSelection? Selection { get; private set; }

    public bool IsFilled => Selection is not null;

    public void Set(VariantSelection selection)
    {
        Selection = selection;
    }

    public void Clear()
    {
        Selection = null;
    }

    public VariantSlotSnapshot ToSnapshot()
    {
        return new VariantSlotSnapshot(Selection?.VariantId, Selection?.Name, Selection?.ProductId, IsFilled);
    }
}

public sealed record VariantSelection(int VariantId, string Name, int ProductId);

public sealed record VariantSlotSnapshot(int? VariantId, string? Name, int? ProductId, bool IsFilled);
