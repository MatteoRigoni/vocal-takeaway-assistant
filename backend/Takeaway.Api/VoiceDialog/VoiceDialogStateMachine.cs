using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Takeaway.Api.VoiceDialog.IntentClassification;

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
    IReadOnlyDictionary<string, string>? Metadata
);

public sealed class VoiceDialogContext
{
    public List<string> RequestedItems { get; } = new();

    public string? OrderCode { get; set; }

    public string? LastPrompt { get; set; }

    public string? LastUtterance { get; set; }

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

    public VoiceDialogState InitialState => VoiceDialogState.Start;

    public Task<VoiceDialogResult> HandleAsync(VoiceDialogSession session, VoiceDialogEvent dialogEvent, CancellationToken cancellationToken)
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

            if (dialogEvent.Metadata?.TryGetValue(IntentMetadataKeys.Confidence, out var confidenceValue) == true)
            {
                session.Context.Metadata[IntentMetadataKeys.Confidence] = confidenceValue;
            }
        }

        return Task.FromResult(dialogEvent.Type switch
        {
            VoiceDialogEventType.Timeout => HandleTimeout(session),
            VoiceDialogEventType.System => HandleSystemEvent(session, dialogEvent.Metadata),
            _ => HandleUtterance(session, normalized, utterance, intentLabel)
        });
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

        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
        session.Context.LastPrompt = prompt;
        return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
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
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
        session.Context.LastPrompt = prompt;
        return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleUtterance(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (session.State is VoiceDialogState.Completed or VoiceDialogState.Cancelled)
        {
            var prompt = "This session is already finished. Say \"start\" if you need to begin again.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, true, session.Context.Metadata);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var prompt = "I didn\'t catch that. Could you repeat it?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.Fallback))
        {
            return HandleFallback(session);
        }

        return session.State switch
        {
            VoiceDialogState.Start => HandleStart(session, normalized, intentLabel),
            VoiceDialogState.Ordering => HandleOrdering(session, normalized, utterance, intentLabel),
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
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "I can cancel an order. What\'s the order code?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.ModifyOrder) || ContainsAny(normalized, "modify", "change", "swap", "edit"))
        {
            session.TransitionTo(VoiceDialogState.Modifying);
            var prompt = "Tell me what needs to change in your order.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.Greeting))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var greetingPrompt = "Hi there! What would you like to order today?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{greetingPrompt}'");
            session.Context.LastPrompt = greetingPrompt;
            return new VoiceDialogResult(session.State, greetingPrompt, false, session.Context.Metadata);
        }

        session.TransitionTo(VoiceDialogState.Ordering);
        var orderingPrompt = session.Context.RequestedItems.Count == 0
            ? "Welcome back! What would you like to order today?"
            : "What else can I add to your order?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{orderingPrompt}'");
        session.Context.LastPrompt = orderingPrompt;
        return new VoiceDialogResult(session.State, orderingPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleOrdering(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var statusPrompt = "Sure, what order code should I look up?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{statusPrompt}'");
            session.Context.LastPrompt = statusPrompt;
            return new VoiceDialogResult(session.State, statusPrompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var cancelPrompt = "Okay, let\'s cancel an order. What\'s the code?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{cancelPrompt}'");
            session.Context.LastPrompt = cancelPrompt;
            return new VoiceDialogResult(session.State, cancelPrompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.ModifyOrder) || ContainsAny(normalized, "change", "modify", "swap"))
        {
            session.TransitionTo(VoiceDialogState.Modifying);
            var modifyPrompt = "Tell me what to change in the order.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{modifyPrompt}'");
            session.Context.LastPrompt = modifyPrompt;
            return new VoiceDialogResult(session.State, modifyPrompt, false, session.Context.Metadata);
        }

        if ((IntentMatches(intentLabel, IntentLabels.CompleteOrder) || IsOrderCompletionCue(normalized))
            && session.Context.RequestedItems.Count > 0)
        {
            session.TransitionTo(VoiceDialogState.Confirming);
            var summary = string.Join(", ", session.Context.RequestedItems);
            var confirmPrompt = $"You\'ve asked for {summary}. Should I place the order?";
            session.Context.Metadata["order.items"] = summary;
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{confirmPrompt}'");
            session.Context.LastPrompt = confirmPrompt;
            return new VoiceDialogResult(session.State, confirmPrompt, false, session.Context.Metadata);
        }

        if ((IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
            && session.Context.RequestedItems.Count == 0)
        {
            var prompt = "No problem. Let me know when you\'re ready to order.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (!string.IsNullOrWhiteSpace(utterance)
            && (intentLabel is null || IntentMatches(intentLabel, IntentLabels.AddItem, IntentLabels.StartOrder)))
        {
            session.Context.RequestedItems.Add(CleanItemDescription(utterance));
        }

        var nextPrompt = "Anything else for the order?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{nextPrompt}'");
        session.Context.LastPrompt = nextPrompt;
        return new VoiceDialogResult(session.State, nextPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleModifying(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "Understood. What order code should I cancel?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = "Sure, what order code should I check?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if ((IntentMatches(intentLabel, IntentLabels.CompleteOrder) || IsOrderCompletionCue(normalized))
            && session.Context.RequestedItems.Count > 0)
        {
            session.TransitionTo(VoiceDialogState.Confirming);
            var summary = string.Join(", ", session.Context.RequestedItems);
            var confirmPrompt = $"Your order now has {summary}. Shall I finalize it?";
            session.Context.Metadata["order.items"] = summary;
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{confirmPrompt}'");
            session.Context.LastPrompt = confirmPrompt;
            return new VoiceDialogResult(session.State, confirmPrompt, false, session.Context.Metadata);
        }

        if (!string.IsNullOrWhiteSpace(utterance))
        {
            session.Context.RequestedItems.Add(CleanItemDescription(utterance));
        }

        var nextPrompt = "Anything else you\'d like to change?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{nextPrompt}'");
        session.Context.LastPrompt = nextPrompt;
        return new VoiceDialogResult(session.State, nextPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleCancelling(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CheckStatus) || ContainsAny(normalized, "status"))
        {
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = "Okay, what order code would you like me to check?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (TryExtractOrderCode(utterance, out var code))
        {
            session.Context.OrderCode = code;
            session.Context.Metadata["order.code"] = code;
            var prompt = $"I found order {code}. Do you want me to cancel it now?";
            session.TransitionTo(VoiceDialogState.Confirming);
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if ((IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
            && session.Context.OrderCode is not null)
        {
            session.TransitionTo(VoiceDialogState.Cancelled);
            var prompt = $"Done. Order {session.Context.OrderCode} has been cancelled.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, true, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "No worries. What else can I help you with?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        var retryPrompt = "Could you share the order code you want to cancel?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{retryPrompt}'");
        session.Context.LastPrompt = retryPrompt;
        return new VoiceDialogResult(session.State, retryPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleCheckingStatus(VoiceDialogSession session, string normalized, string utterance, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.CancelOrder) || ContainsAny(normalized, "cancel"))
        {
            session.TransitionTo(VoiceDialogState.Cancelling);
            var prompt = "Okay, I can cancel it. What order code is it?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if (TryExtractOrderCode(utterance, out var code))
        {
            session.Context.OrderCode = code;
            session.Context.Metadata["order.code"] = code;
            session.TransitionTo(VoiceDialogState.CheckingStatus);
            var prompt = $"Order {code} is currently being prepared. Anything else you need?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        if ((IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
            && session.Context.OrderCode is not null)
        {
            var prompt = $"Order {session.Context.OrderCode} is ready for pickup.";
            session.TransitionTo(VoiceDialogState.Completed);
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, true, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "Alright. Do you want to place a new order?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        var askPrompt = "Please provide the order code so I can look it up.";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{askPrompt}'");
        session.Context.LastPrompt = askPrompt;
        return new VoiceDialogResult(session.State, askPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleConfirming(VoiceDialogSession session, string normalized, string? intentLabel)
    {
        if (IntentMatches(intentLabel, IntentLabels.Affirm) || ContainsAffirmation(normalized))
        {
            if (session.Context.Metadata.TryGetValue("order.code", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                session.TransitionTo(VoiceDialogState.Cancelled);
                var prompt2 = $"Done. Order {code} is cancelled.";
                // LOG: tracciamo ogni nuovo prompt generato dal dialogo
                Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt2}'");
                session.Context.LastPrompt = prompt2;
                return new VoiceDialogResult(session.State, prompt2, true, session.Context.Metadata);
            }

            session.TransitionTo(VoiceDialogState.Completed);
            var orderCode = session.Context.OrderCode ?? GenerateOrderCode();
            session.Context.OrderCode = orderCode;
            session.Context.Metadata["order.code"] = orderCode;
            var prompt = $"Your order is confirmed. The pickup code is {orderCode}.";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, true, session.Context.Metadata);
        }

        if (IntentMatches(intentLabel, IntentLabels.Negate) || ContainsNegation(normalized))
        {
            session.TransitionTo(VoiceDialogState.Ordering);
            var prompt = "No problem. What should we adjust?";
            // LOG: tracciamo ogni nuovo prompt generato dal dialogo
            Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
            session.Context.LastPrompt = prompt;
            return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
        }

        var neutralPrompt = "Just to confirm, should I go ahead?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{neutralPrompt}'");
        session.Context.LastPrompt = neutralPrompt;
        return new VoiceDialogResult(session.State, neutralPrompt, false, session.Context.Metadata);
    }

    private static VoiceDialogResult HandleFallback(VoiceDialogSession session)
    {
        session.TransitionTo(VoiceDialogState.Error);
        var prompt = "I\'m not sure how to handle that. Let\'s start over. What do you need help with?";
        // LOG: tracciamo ogni nuovo prompt generato dal dialogo
        Console.WriteLine($"[VoiceDialogStateMachine] Returning prompt (state: {session.State}): '{prompt}'");
        session.Context.LastPrompt = prompt;
        return new VoiceDialogResult(session.State, prompt, false, session.Context.Metadata);
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
