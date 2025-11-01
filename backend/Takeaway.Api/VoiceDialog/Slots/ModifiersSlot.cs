using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Takeaway.Api.VoiceDialog.Slots;

public sealed class ModifiersSlot
{
    private readonly List<ModifierSelection> _selections = new();

    public bool IsFilled { get; private set; }

    public bool IsExplicitNone { get; private set; }

    public IReadOnlyList<ModifierSelection> Selections => new ReadOnlyCollection<ModifierSelection>(_selections);

    public void Set(IEnumerable<ModifierSelection> selections)
    {
        _selections.Clear();
        _selections.AddRange(selections);
        IsFilled = true;
        IsExplicitNone = !_selections.Any();
    }

    public void MarkNone()
    {
        _selections.Clear();
        IsFilled = true;
        IsExplicitNone = true;
    }

    public void Clear()
    {
        _selections.Clear();
        IsFilled = false;
        IsExplicitNone = false;
    }

    public ModifiersSlotSnapshot ToSnapshot()
    {
        return new ModifiersSlotSnapshot(_selections.ToList(), IsFilled, IsExplicitNone);
    }
}

public sealed record ModifierSelection(int ModifierId, string Name, int ProductId);

public sealed record ModifiersSlotSnapshot(IReadOnlyList<ModifierSelection> Selections, bool IsFilled, bool IsExplicitNone);
