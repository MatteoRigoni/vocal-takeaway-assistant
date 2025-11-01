using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Takeaway.Api.VoiceDialog;

public sealed class VoiceOrderItemDraft
{
    public VoiceOrderItemDraft(
        string rawText,
        string productName,
        string normalizedProductName,
        string? variantName,
        string? normalizedVariantName,
        IReadOnlyCollection<string> modifiers,
        IReadOnlyCollection<string> normalizedModifiers,
        int quantity)
    {
        RawText = rawText;
        ProductName = productName;
        NormalizedProductName = normalizedProductName;
        VariantName = variantName;
        NormalizedVariantName = normalizedVariantName;
        Modifiers = modifiers;
        NormalizedModifiers = normalizedModifiers;
        Quantity = quantity;
    }

    public string RawText { get; }

    public string ProductName { get; }

    public string NormalizedProductName { get; }

    public string? VariantName { get; }

    public string? NormalizedVariantName { get; }

    public IReadOnlyCollection<string> Modifiers { get; }

    public IReadOnlyCollection<string> NormalizedModifiers { get; }

    public int Quantity { get; private set; }

    public void IncreaseQuantity(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Quantity += amount;
    }

    public string ToSummaryString(CultureInfo? culture = null)
    {
        var cultureInfo = culture ?? CultureInfo.CurrentCulture;
        var modifierSuffix = Modifiers.Count > 0
            ? $" with {string.Join(", ", Modifiers)}"
            : string.Empty;

        var variantPrefix = string.IsNullOrWhiteSpace(VariantName)
            ? string.Empty
            : $"{VariantName} ";

        return $"{Quantity.ToString(cultureInfo)}x {variantPrefix}{ProductName}{modifierSuffix}".Trim();
    }

    public string ToMetadataString()
    {
        var variantPart = string.IsNullOrWhiteSpace(VariantName) ? string.Empty : $"|variant={VariantName}";
        var modifiersPart = Modifiers.Count > 0 ? $"|modifiers={string.Join(';', Modifiers)}" : string.Empty;
        return $"{Quantity}x {ProductName}{variantPart}{modifiersPart}";
    }

    public static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[index++] = char.ToLowerInvariant(c);
            }
        }

        return index == 0 ? string.Empty : new string(buffer[..index]);
    }
}

public sealed class VoiceOrderDraft
{
    private readonly List<VoiceOrderItemDraft> _items = new();

    public int ShopId { get; set; } = 1;

    public int OrderChannelId { get; set; } = 1;

    public IReadOnlyList<VoiceOrderItemDraft> Items => _items;

    public DateTimeOffset? PickupTime { get; private set; }

    public string? PickupPhrase { get; private set; }

    public bool HasPickupTime => PickupTime.HasValue;

    public bool AreRequiredSlotsFilled => _items.Count > 0 && PickupTime.HasValue;

    public void AddOrUpdateItem(VoiceOrderItemDraft item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var existing = _items.FirstOrDefault(i =>
            string.Equals(i.NormalizedProductName, item.NormalizedProductName, StringComparison.Ordinal)
            && string.Equals(i.NormalizedVariantName, item.NormalizedVariantName, StringComparison.Ordinal)
            && HaveSameModifiers(i.NormalizedModifiers, item.NormalizedModifiers));

        if (existing is not null)
        {
            existing.IncreaseQuantity(item.Quantity);
            return;
        }

        _items.Add(item);
    }

    public bool SetPickupTime(DateTimeOffset pickupTime, string? pickupPhrase)
    {
        var changed = !PickupTime.HasValue || PickupTime.Value != pickupTime;
        PickupTime = pickupTime;
        PickupPhrase = pickupPhrase;
        return changed;
    }

    public void Clear()
    {
        _items.Clear();
        PickupTime = null;
        PickupPhrase = null;
    }

    public string BuildSummary(CultureInfo? culture = null)
    {
        if (_items.Count == 0)
        {
            return "";
        }

        var cultureInfo = culture ?? CultureInfo.CurrentCulture;
        var itemParts = _items.Select(i => i.ToSummaryString(cultureInfo));
        var summary = string.Join(", ", itemParts);

        if (PickupTime.HasValue)
        {
            var pickupLocal = PickupTime.Value.ToLocalTime();
            summary += $" for pickup at {pickupLocal:HH:mm}";
        }

        return summary;
    }

    private static bool HaveSameModifiers(
        IReadOnlyCollection<string> first,
        IReadOnlyCollection<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        if (first.Count == 0)
        {
            return true;
        }

        return first.All(m => second.Contains(m));
    }
}
