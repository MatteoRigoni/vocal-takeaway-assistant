using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Data;
using Takeaway.Api.Services;
using Takeaway.Api.VoiceDialog.IntentClassification;
using Takeaway.Api.VoiceDialog.Slots;

namespace Takeaway.Api.VoiceDialog;

public enum VoiceDialogState
{
    Start,
    Ordering,
    Modifying,
    Cancelling,
    CheckingStatus,
    Confirming,
    Completed,
    Cancelled,
    Error
}

public enum VoiceDialogEventType
{
    Utterance,
    System,
    Timeout
}

public sealed record VoiceDialogEvent(VoiceDialogEventType Type, string? UtteranceText = null, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record VoiceDialogResult(
    VoiceDialogState State,
    string PromptText,
    bool IsSessionComplete,
    IReadOnlyDictionary<string, string>? Metadata,
    VoiceOrderSlotsSnapshot Slots
);

public sealed class VoiceDialogContext
{
    public List<string> RequestedItems { get; } = new();

    public string? OrderCode { get; set; }

    public string? LastPrompt { get; set; }

    public string? LastUtterance { get; set; }

    public VoiceOrderSlots Slots { get; } = new();

    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VoiceDialogSession
{
    public VoiceDialogSession(string sessionId)
    {
        Id = sessionId;
        State = VoiceDialogState.Start;
        Context = new VoiceDialogContext();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public VoiceDialogState State { get; private set; }

    public VoiceDialogContext Context { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void TransitionTo(VoiceDialogState state)
    {
        State = state;
        Touch();
    }

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public interface IVoiceDialogStateMachine
{
    VoiceDialogState InitialState { get; }

    Task<VoiceDialogResult> HandleAsync(VoiceDialogSession session, VoiceDialogEvent dialogEvent, CancellationToken cancellationToken);
}

public sealed class VoiceDialogStateMachine : IVoiceDialogStateMachine
{
    private static readonly Regex OrderCodeRegex = new("(?<code>[A-Za-z]{2,}-?\\d{2,})", RegexOptions.Compiled);
    private static readonly Regex QuantityRegex = new("\\b(\\d{1,2})\\b", RegexOptions.Compiled);
    private static readonly Dictionary<string, int> QuantityWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1,
        ["an"] = 1,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10
    };
    private static readonly char[] TokenSeparators = { ' ', ',', '.', '!', '?', ';', ':', '\'', '"' };
    private readonly TakeawayDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    private const string SlotProductIdKey = "slot.product.id";
    private const string SlotProductNameKey = "slot.product.name";
    private const string SlotVariantIdKey = "slot.variant.id";
    private const string SlotVariantNameKey = "slot.variant.name";
    private const string SlotQuantityKey = "slot.quantity";
    private const string SlotModifiersKey = "slot.modifiers";
    private const string SlotPickupTimeKey = "slot.pickup";

    public VoiceDialogStateMachine(TakeawayDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public VoiceDialogState InitialState => VoiceDialogState.Start;

    public async Task<VoiceDialogResult> HandleAsync(VoiceDialogSession session, VoiceDialogEvent dialogEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialogEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var utterance = (dialogEvent.UtteranceText ?? string.Empty).Trim();
        var normalized = utterance.ToLowerInvariant();
        var intentLabel = GetIntentLabel(dialogEvent.Metadata);

        session.Context.LastUtterance = utterance;

        if (!string.IsNullOrWhiteSpace(intentLabel))
        {
            session.Context.Metadata[IntentMetadataKeys.LastLabel] = intentLabel;

            if (dialogEvent.Metadata?.TryGetValue(IntentMetadataKeys.Confidence, out var confidenceValue))
            {
                session.Context.Metadata[IntentMetadataKeys.Confidence] = confidenceValue;
            }
        }

        return dialogEvent.Type switch
        {
            VoiceDialogEventType.Timeout => HandleTimeout(session),
            VoiceDialogEventType.System => HandleSystemEvent(session, dialogEvent.Metadata),
            _ => await HandleUtteranceAsync(session, normalized, utterance, intentLabel, cancellationToken)
        };
    }

    private static VoiceDialogResult HandleTimeout(VoiceDialogSession session)
    {
        var prompt = session.State switch
        {
            VoiceDialogState.Ordering => "I\'m still here. Would you like to add anything else to your order?",
            VoiceDialogState.Modifying => "Do you want to change something else in the order?",
            VoiceDialogState.Cancelling => "I can help cancel the order. Could you share the order code?",
            VoiceDialogState.CheckingStatus => "Please tell me the order code so I can check its status.",
            _ => "Are you still there?"
        };

        return BuildResult(session, prompt, false);
    }

    private static VoiceDialogResult HandleSystemEvent(VoiceDialogSession session, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                session.Context.Metadata[pair.Key] = pair.Value;
            }
        }

        var prompt = session.Context.LastPrompt ?? "How can I help you with your takeaway order today?";
        return BuildResult(session, prompt, false);
    }

    private async Task<VoiceDialogResult> HandleUtteranceAsync(VoiceDialogSession session, string normalized, string utterance, string? intentLabel, CancellationToken cancellationToken)
    {
        if (session.State is VoiceDialogState.Completed or VoiceDialogState.Cancelled)
        {
            var prompt = "This session is already finished. Say \"start\" if you need to begin again.";
            return BuildResult(session, prompt, true);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var prompt = "I didn\'t catch that. Could you repeat it?";
            return BuildResult(session, prompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.Fallback))
        {
            return HandleFallback(session);
        }

        return session.State switch
        {
            VoiceDialogState.Start => HandleStart(session, normalized, intentLabel),
            VoiceDialogState.Ordering => await HandleOrderingAsync(session, normalized, utterance, intentLabel, cancellationToken),
            VoiceDialogState.Modifying => HandleModifying(session, normalized, utterance, intentLabel),
            VoiceDialogState.Cancelling => HandleCancelling(session, normalized, utterance, intentLabel),
            VoiceDialogState.CheckingStatus => HandleCheckingStatus(session, normalized, utterance, intentLabel),
            VoiceDialogState.Confirming => HandleConfirming(session, normalized, intentLabel),
            _ => HandleFallback(session)
        };
    }

    private static VoiceDialogResult HandleStart(VoiceDialogSession session, string normalized, string? intentLabel)
    {
        session.TransitionTo(VoiceDialogState.Start);

        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status", "where", "ready", "pickup"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = "Sure, what order code should I check for you?";
            return BuildResult(session, prompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "I can cancel an order. What\'s the order code?";
            return BuildResult(session, prompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.ModifyOrder) || ContainsAny(normalized, "modify", "change", "swap", "edit"))
        {
            session.TransitionTo(VoiceDialogState.Modifying);
            var prompt = "Tell me what needs to change in your order.";
            return BuildResult(session, prompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.Greeting))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var greetingPrompt = "Hi there! What would you like to order today?";
            return BuildResult(session, greetingPrompt, false);
        }

        session.TransitionTo(VoiceDialogState.Ordering);
        var orderingPrompt = session.Context.RequestedItems.Count == 0
            ? "Welcome back! What would you like to order today?"
            : "What else can I add to your order?";
        return BuildResult(session, orderingPrompt, false);
    }

    private async Task<VoiceDialogResult> HandleOrderingAsync(VoiceDialogSession session, string normalized, string utterance, string? intentLabel, CancellationToken cancellationToken)
    {
        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var statusPrompt = "Sure, what order code should I look up?";
            return BuildResult(session, statusPrompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var cancelPrompt = "Okay, let\'s cancel an order. What\'s the code?";
            return BuildResult(session, cancelPrompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.ModifyOrder) || ContainsAny(normalized, "change", "modify", "swap"))
        {
            session.TransitionTo(VoiceDialogState.Modifying);
            var modifyPrompt = "Tell me what to change in the order.";
            return BuildResult(session, modifyPrompt, false);
        }

        var menu = await LoadMenuSnapshotAsync(cancellationToken);
        ApplyUtteranceToSlots(session, utterance, normalized, intentLabel, menu);

        var slots = session.Context.Slots;

        if (slots.Product.Selection is null)
        {
            var suggestions = menu.GetTopProductNames(3).ToList();
            var prompt = suggestions.Count > 0
                ? $"What would you like to order? Popular choices are {string.Join(", ", suggestions)}."
                : "What would you like to order from the menu?";
            return BuildResult(session, prompt, false);
        }

        if (!menu.TryGetProduct(slots.Product.Selection.ProductId, out var product))
        {
            slots.ClearProduct();
            var prompt = "I couldn\'t find that product on the menu. Could you choose something else?";
            return BuildResult(session, prompt, false);
        }

        EnsureDefaultVariant(slots, product);

        if (product.Variants.Count > 1)
        {
            if (slots.Variant.Selection is not { } variant || variant.ProductId != product.Id)
            {
                var options = string.Join(", ", product.Variants.Select(v => v.Name));
                var prompt = $"Which variant of {product.Name} would you like? Options are {options}.";
                return BuildResult(session, prompt, false);
            }
        }

        if (!slots.Quantity.Value.HasValue)
        {
            var prompt = $"How many {product.Name} should I prepare?";
            return BuildResult(session, prompt, false);
        }

        if (product.Modifiers.Count > 0 && !slots.Modifiers.IsFilled)
        {
            var options = string.Join(", ", product.Modifiers.Select(m => m.Name));
            var prompt = $"Would you like any modifiers for {product.Name}? Available: {options}.";
            return BuildResult(session, prompt, false);
        }

        if (!slots.PickupTime.IsFilled)
        {
            var prompt = "When should it be ready for pickup?";
            return BuildResult(session, prompt, false);
        }

        var summary = BuildOrderSummary(slots, product);
        session.TransitionTo(VoiceDialogState.Confirming);
        session.Context.Metadata["order.items"] = summary;
        session.Context.RequestedItems.Clear();
        session.Context.RequestedItems.Add(summary);

        var confirmPrompt = $"Great, {summary}. Shall I place the order?";
        return BuildResult(session, confirmPrompt, false);
    }

    private static VoiceDialogResult HandleModifying(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "Understood. What order code should I cancel?";
            return BuildResult(session, prompt, false);
        }

        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = "Sure, what order code should I check?";
            return BuildResult(session, prompt, false);
        }

        if ((IntentMatches(intentLabel, IntentLabels.CompleteOrder) || IsOrderCompletionCue(normalized))
            && session.Context.RequestedItems.Count > 0)
        {
            session.TransitionTo(VoiceDialogState.Confirming);
            var summary = string.Join(", ", session.Context.RequestedItems);
            var confirmPrompt = $"Your order now has {summary}. Shall I finalize it?";
            session.Context.Metadata["order.items"] = summary;
            return BuildResult(session, confirmPrompt, false);
        }

        if (!string.IsNullOrWhiteSpace(utterance))
        {
            session.Context.RequestedItems.Add(CleanItemDescription(utterance));
        }

        var nextPrompt = "Anything else you\'d like to change?";
        return BuildResult(session, nextPrompt, false);
    }

    private static VoiceDialogResult HandleCancelling(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = "Okay, what order code would you like me to check?";
            return BuildResult(session, prompt, false);
        }

        if (TryExtractOrderCode(utterance, out var code))
        {
            session.Context.OrderCode = code;
            session.Context.Metadata["order.code"] = code;
            var prompt = $"I found order {code}. Do you want me to cancel it now?";
            session.TransitionTo(VoiceDialogState.Confirming);
            return BuildResult(session, prompt, false);
        }

        if ((IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
            && session.Context.OrderCode is not null)
        {
            session.TransitionTo(VoiceDialogState.Cancelled);
            var prompt = $"Done. Order {session.Context.OrderCode} has been cancelled.";
            return BuildResult(session, prompt, true);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "No worries. What else can I help you with?";
            return BuildResult(session, prompt, false);
        }

        var retryPrompt = "Could you share the order code you want to cancel?";
        return BuildResult(session, retryPrompt, false);
    }

    private static VoiceDialogResult HandleCheckingStatus(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "Okay, I can cancel it. What order code is it?";
            return BuildResult(session, prompt, false);
        }

        if (TryExtractOrderCode(utterance, out var code))
        {
            session.Context.OrderCode = code;
            session.Context.Metadata["order.code"] = code;
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = $"Order {code} is currently being prepared. Anything else you need?";
            return BuildResult(session, prompt, false);
        }

        if ((IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
            && session.Context.OrderCode is not null)
        {
            var prompt = $"Order {session.Context.OrderCode} is ready for pickup.";
            session.TransitionTo(VoiceDialogState.Completed);
            return BuildResult(session, prompt, true);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "Alright. Do you want to place a new order?";
            return BuildResult(session, prompt, false);
        }

        var askPrompt = "Please provide the order code so I can look it up.";
        return BuildResult(session, askPrompt, false);
    }

    private static VoiceDialogResult HandleConfirming(VoiceDialogSession session, string normalized, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
        {
            if (session.Context.Metadata.TryGetValue("order.code", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                session.TransitionTo(VoiceDialogState.Cancelled);
                var prompt2 = $"Done. Order {code} is cancelled.";
                return BuildResult(session, prompt2, true);
            }

            session.TransitionTo(VoiceDialogState.Completed);
            var orderCode = session.Context.OrderCode ?? GenerateOrderCode();
            session.Context.OrderCode = orderCode;
            session.Context.Metadata["order.code"] = orderCode;
            var prompt = $"Your order is confirmed. The pickup code is {orderCode}.";
            return BuildResult(session, prompt, true);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "No problem. What should we adjust?";
            return BuildResult(session, prompt, false);
        }

        var neutralPrompt = "Just to confirm, should I go ahead?";
        return BuildResult(session, neutralPrompt, false);
    }

    private static VoiceDialogResult HandleFallback(VoiceDialogSession session)
    {
        session.TransitionTo(VoiceDialogState.Error);
        var prompt = "I\'m not sure how to handle that. Let\'s start over. What do you need help with?";
        return BuildResult(session, prompt, false);
    }

    private static VoiceDialogResult BuildResult(VoiceDialogSession session, string prompt, bool isComplete)
    {
        session.Context.LastPrompt = prompt;
        UpdateSlotMetadata(session);
        return new VoiceDialogResult(session.State, prompt, isComplete, session.Context.Metadata, session.Context.Slots.ToSnapshot());
    }

    private static void UpdateSlotMetadata(VoiceDialogSession session)
    {
        var metadata = session.Context.Metadata;
        var slots = session.Context.Slots;

        if (slots.Product.Selection is { } product)
        {
            metadata[SlotProductIdKey] = product.ProductId.ToString(CultureInfo.InvariantCulture);
            metadata[SlotProductNameKey] = product.Name;
        }
        else
        {
            metadata.Remove(SlotProductIdKey);
            metadata.Remove(SlotProductNameKey);
        }

        if (slots.Variant.Selection is { } variant)
        {
            metadata[SlotVariantIdKey] = variant.VariantId.ToString(CultureInfo.InvariantCulture);
            metadata[SlotVariantNameKey] = variant.Name;
        }
        else
        {
            metadata.Remove(SlotVariantIdKey);
            metadata.Remove(SlotVariantNameKey);
        }

        if (slots.Quantity.Value is int quantity)
        {
            metadata[SlotQuantityKey] = quantity.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            metadata.Remove(SlotQuantityKey);
        }

        if (slots.Modifiers.IsFilled)
        {
            metadata[SlotModifiersKey] = slots.Modifiers.IsExplicitNone
                ? "(none)"
                : string.Join(",", slots.Modifiers.Selections.Select(m => m.Name));
        }
        else
        {
            metadata.Remove(SlotModifiersKey);
        }

        if (slots.PickupTime.Value is DateTimeOffset pickup)
        {
            metadata[SlotPickupTimeKey] = pickup.ToString("o", CultureInfo.InvariantCulture);
        }
        else
        {
            metadata.Remove(SlotPickupTimeKey);
        }
    }

    private void ApplyUtteranceToSlots(VoiceDialogSession session, string utterance, string normalized, string? intentLabel, MenuSnapshot menu)
    {
        var slots = session.Context.Slots;
        var normalizedCompact = NormalizeForLookup(utterance);

        if (menu.TryMatchProduct(utterance, normalizedCompact, out var matchedProduct))
        {
            slots.SetProduct(new ProductSelection(matchedProduct.Id, matchedProduct.Name));
        }

        if (slots.Product.Selection is null)
        {
            return;
        }

        if (!menu.TryGetProduct(slots.Product.Selection.ProductId, out var product))
        {
            return;
        }

        if (product.Variants.Count > 1 && TryMatchVariant(utterance, normalizedCompact, product, out var variant))
        {
            slots.SetVariant(new VariantSelection(variant.Id, variant.Name, product.Id));
        }

        if (TryParseQuantity(utterance, out var quantity) && SlotValidation.IsValidQuantity(quantity))
        {
            slots.SetQuantity(quantity);
        }

        if (product.Modifiers.Count == 0)
        {
            slots.MarkNoModifiers();
        }
        else
        {
            var selectedModifiers = product.Modifiers
                .Where(m => ContainsPhrase(utterance, m.Name) || normalizedCompact.Contains(m.NormalizedName, StringComparison.Ordinal))
                .Select(m => new ModifierSelection(m.Id, m.Name, product.Id))
                .ToList();

            if (selectedModifiers.Count > 0)
            {
                slots.SetModifiers(selectedModifiers);
            }
            else if (!slots.Modifiers.IsFilled && (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized)))
            {
                slots.MarkNoModifiers();
            }
        }

        if (!slots.PickupTime.IsFilled && TryParsePickupTime(utterance, out var pickupTime))
        {
            slots.SetPickupTime(pickupTime);
        }
    }

    private static void EnsureDefaultVariant(VoiceOrderSlots slots, MenuProductSnapshot product)
    {
        if (product.Variants.Count == 0)
        {
            slots.ClearVariant();
            return;
        }

        if (product.Variants.Count == 1)
        {
            var single = product.Variants[0];
            slots.SetVariant(new VariantSelection(single.Id, single.Name, product.Id));
            return;
        }

        if (slots.Variant.Selection is null || slots.Variant.Selection.ProductId != product.Id)
        {
            var defaultVariant = product.Variants.FirstOrDefault(v => v.IsDefault);
            if (defaultVariant is not null)
            {
                slots.SetVariant(new VariantSelection(defaultVariant.Id, defaultVariant.Name, product.Id));
            }
        }
    }

    private static bool TryMatchVariant(string utterance, string normalizedCompact, MenuProductSnapshot product, out MenuVariantSnapshot variant)
    {
        foreach (var candidate in product.Variants)
        {
            if (ContainsPhrase(utterance, candidate.Name) || normalizedCompact.Contains(candidate.NormalizedName, StringComparison.Ordinal))
            {
                variant = candidate;
                return true;
            }
        }

        variant = null!;
        return false;
    }

    private static string BuildOrderSummary(VoiceOrderSlots slots, MenuProductSnapshot product)
    {
        var quantity = Math.Max(1, slots.Quantity.Value.GetValueOrDefault(1));
        var builder = new StringBuilder();
        builder.Append(quantity);
        builder.Append(quantity == 1 ? " x " : " x ");
        builder.Append(product.Name);

        if (slots.Variant.Selection is { } variant && variant.ProductId == product.Id)
        {
            builder.Append(" (");
            builder.Append(variant.Name);
            builder.Append(')');
        }

        if (slots.Modifiers.IsFilled && !slots.Modifiers.IsExplicitNone && slots.Modifiers.Selections.Count > 0)
        {
            builder.Append(" with ");
            builder.Append(string.Join(" and ", slots.Modifiers.Selections.Select(m => m.Name)));
        }

        if (slots.PickupTime.Value is DateTimeOffset pickup)
        {
            builder.Append(", ready at ");
            builder.Append(pickup.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
        }

        return builder.ToString();
    }

    private async Task<MenuSnapshot> LoadMenuSnapshotAsync(CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.IsAvailable)
            .Include(p => p.Variants)
            .Include(p => p.Modifiers)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var snapshots = products.Select(product => new MenuProductSnapshot(
            product.Id,
            product.Name,
            product.Variants
                .OrderBy(v => v.Name)
                .Select(v => new MenuVariantSnapshot(v.Id, v.Name, v.IsDefault))
                .ToList(),
            product.Modifiers
                .OrderBy(m => m.Name)
                .Select(m => new MenuModifierSnapshot(m.Id, m.Name))
                .ToList()))
            .ToList();

        return new MenuSnapshot(snapshots);
    }

    private bool TryParsePickupTime(string utterance, out DateTimeOffset pickupTime)
    {
        pickupTime = default;

        if (string.IsNullOrWhiteSpace(utterance))
        {
            return false;
        }

        var now = _dateTimeProvider.UtcNow;
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;

        if (DateTime.TryParse(utterance, CultureInfo.CurrentCulture, styles, out var parsed)
            || DateTime.TryParse(utterance, CultureInfo.InvariantCulture, styles, out parsed))
        {
            var local = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            if (local < now.ToLocalTime())
            {
                local = local.AddDays(1);
            }

            var candidate = new DateTimeOffset(local);
            if (SlotValidation.IsValidPickupTime(candidate, now))
            {
                pickupTime = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseQuantity(string utterance, out int quantity)
    {
        quantity = default;
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return false;
        }

        var match = QuantityRegex.Match(utterance);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            quantity = numeric;
            return true;
        }

        var tokens = utterance.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (QuantityWords.TryGetValue(token.ToLowerInvariant(), out var wordValue))
            {
                quantity = wordValue;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeForLookup(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static bool ContainsPhrase(string source, string phrase)
    {
        return !string.IsNullOrWhiteSpace(phrase)
            && source.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class MenuSnapshot
    {
        private readonly IReadOnlyList<MenuProductSnapshot> _products;
        private readonly Dictionary<int, MenuProductSnapshot> _productsById;

        public MenuSnapshot(IReadOnlyList<MenuProductSnapshot> products)
        {
            _products = products;
            _productsById = products.ToDictionary(p => p.Id);
        }

        public bool TryMatchProduct(string utterance, string normalizedCompact, out MenuProductSnapshot product)
        {
            foreach (var candidate in _products)
            {
                if (ContainsPhrase(utterance, candidate.Name) || normalizedCompact.Contains(candidate.NormalizedName, StringComparison.Ordinal))
                {
                    product = candidate;
                    return true;
                }
            }

            product = null!;
            return false;
        }

        public bool TryGetProduct(int productId, out MenuProductSnapshot product)
        {
            return _productsById.TryGetValue(productId, out product);
        }

        public IEnumerable<string> GetTopProductNames(int count)
        {
            return _products.Take(count).Select(p => p.Name);
        }
    }

    private sealed class MenuProductSnapshot
    {
        public MenuProductSnapshot(int id, string name, IReadOnlyList<MenuVariantSnapshot> variants, IReadOnlyList<MenuModifierSnapshot> modifiers)
        {
            Id = id;
            Name = name;
            NormalizedName = NormalizeForLookup(name);
            Variants = variants;
            Modifiers = modifiers;
        }

        public int Id { get; }
        public string Name { get; }
        public string NormalizedName { get; }
        public IReadOnlyList<MenuVariantSnapshot> Variants { get; }
        public IReadOnlyList<MenuModifierSnapshot> Modifiers { get; }
    }

    private sealed class MenuVariantSnapshot
    {
        public MenuVariantSnapshot(int id, string name, bool isDefault)
        {
            Id = id;
            Name = name;
            IsDefault = isDefault;
            NormalizedName = NormalizeForLookup(name);
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsDefault { get; }
        public string NormalizedName { get; }
    }

    private sealed class MenuModifierSnapshot
    {
        public MenuModifierSnapshot(int id, string name)
        {
            Id = id;
            Name = name;
            NormalizedName = NormalizeForLookup(name);
        }

        public int Id { get; }
        public string Name { get; }
        public string NormalizedName { get; }
    }

    private static string? GetIntentLabel(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata.TryGetValue(IntentMetadataKeys.Label, out var label) && !string.IsNullOrWhiteSpace(label)
            ? label
            : null;
    }

    private static bool IntentMatches(string? intentLabel, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(intentLabel) || candidates is null || candidates.Length == 0)
        {
            return false;
        }

        return candidates.Any(candidate => string.Equals(intentLabel, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanItemDescription(string utterance)
    {
        var tokens = utterance.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return utterance.Trim();
        }

        var filtered = tokens.Where(t => !StopWords.Contains(t));
        var cleaned = string.Join(' ', filtered);
        return string.IsNullOrWhiteSpace(cleaned) ? utterance.Trim() : cleaned;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAffirmation(string text)
    {
        return ContainsAny(text, "yes", "yep", "correct", "confirm", "sure", "do it", "go ahead");
    }

    private static bool ContainsNegation(string text)
    {
        return ContainsAny(text, "no", "not", "wait", "hold on", "stop", "cancel that", "nevermind");
    }

    private static bool IsOrderCompletionCue(string text)
    {
        return ContainsAny(text, "done", "finish", "that\'s all", "that\'s it", "place the order", "ready to pay", "confirm");
    }

    private static bool TryExtractOrderCode(string utterance, out string? code)
    {
        var match = OrderCodeRegex.Match(utterance.ToUpperInvariant());
        if (match.Success)
        {
            code = match.Groups["code"].Value;
            return true;
        }

        var digits = new string(utterance.Where(char.IsLetterOrDigit).ToArray());
        if (digits.Length >= 4)
        {
            code = FormatOrderCode(digits);
            return true;
        }

        code = null;
        return false;
    }

    private static string FormatOrderCode(string raw)
    {
        if (raw.Length <= 4)
        {
            return raw.ToUpperInvariant();
        }

        return $"TA-{raw[^4..].ToUpperInvariant()}";
    }

    private static string GenerateOrderCode()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        return $"TA-{timestamp}";
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "would", "like", "to", "a", "an", "and", "please", "order", "get", "me", "for", "just", "with", "without"
    };
}
