namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class ProductSlot
{
    public ProductSelection? Selection { get; private set; }

    public bool IsFilled => Selection is not null;

    public void Set(ProductSelection selection)
    {
        Selection = selection;
    }

    public void Clear()
    {
        Selection = null;
    }

    public ProductSlotSnapshot ToSnapshot()
    {
        return new ProductSlotSnapshot(Selection?.ProductId, Selection?.Name, IsFilled);
    }
}

public sealed record ProductSelection(int ProductId, string Name);

public sealed record ProductSlotSnapshot(int? ProductId, string? Name, bool IsFilled);
