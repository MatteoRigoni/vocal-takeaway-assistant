using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Takeaway.Api.VoiceDialog;
using Takeaway.Api.VoiceDialog.IntentClassification;
using Xunit;

namespace Takeaway.Api.Tests.Unit;

public sealed class VoiceDialogStateMachineTests
{
    private readonly VoiceDialogStateMachine _stateMachine = new();

    [Fact]
    public async Task HandleAsync_StartWithOrderIntent_TransitionsToOrdering()
    {
        var session = CreateSession();

        var result = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("I want to start an order", IntentLabels.StartOrder, confidence: "0.930"),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Ordering, session.State);
        Assert.Equal(VoiceDialogState.Ordering, result.State);
        Assert.False(result.IsSessionComplete);
        Assert.Equal("Welcome back! What would you like to order today?", result.PromptText);
        Assert.Equal(IntentLabels.StartOrder, session.Context.Metadata[IntentMetadataKeys.LastLabel]);
        Assert.Equal("0.930", session.Context.Metadata[IntentMetadataKeys.Confidence]);
    }

    [Fact]
    public async Task HandleAsync_OrderingAddsItemAndUpsells()
    {
        var session = await CreateOrderingSessionAsync();

        var result = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("I would like a spicy taco", IntentLabels.AddItem),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Ordering, session.State);
        Assert.Equal("Anything else for the order?", result.PromptText);
        Assert.Contains("spicy taco", session.Context.RequestedItems);
        Assert.Equal(IntentLabels.AddItem, session.Context.Metadata[IntentMetadataKeys.LastLabel]);
    }

    [Fact]
    public async Task HandleAsync_CompleteOrderIntent_TransitionsToConfirmingWithSummary()
    {
        var session = await CreateOrderingSessionAsync();
        await _stateMachine.HandleAsync(
            session,
            CreateUtterance("Add a spicy taco", IntentLabels.AddItem),
            CancellationToken.None);

        var result = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("that will be everything", IntentLabels.CompleteOrder),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Confirming, session.State);
        Assert.Equal(VoiceDialogState.Confirming, result.State);
        Assert.Contains("Should I place the order?", result.PromptText);
        Assert.Equal("spicy taco", session.Context.Metadata["order.items"]);
    }

    [Fact]
    public async Task HandleAsync_ConfirmingAffirmation_FinalizesOrder()
    {
        var session = await CreateOrderingSessionAsync();
        await _stateMachine.HandleAsync(session, CreateUtterance("Add a falafel wrap", IntentLabels.AddItem), CancellationToken.None);
        await _stateMachine.HandleAsync(session, CreateUtterance("we are done", IntentLabels.CompleteOrder), CancellationToken.None);

        var result = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("yes please", IntentLabels.Affirm),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Completed, session.State);
        Assert.True(result.IsSessionComplete);
        Assert.Contains("Your order is confirmed", result.PromptText);
        Assert.True(session.Context.Metadata.TryGetValue("order.code", out var orderCode));
        Assert.False(string.IsNullOrWhiteSpace(orderCode));
    }

    [Fact]
    public async Task HandleAsync_CancellationFlow_ValidatesOrderCodeAndCancels()
    {
        var session = CreateSession();
        await _stateMachine.HandleAsync(session, CreateUtterance("cancel my order", IntentLabels.CancelOrder), CancellationToken.None);

        var retry = await _stateMachine.HandleAsync(session, CreateUtterance("abc", null), CancellationToken.None);

        Assert.Equal(VoiceDialogState.Cancelling, session.State);
        Assert.Equal("Could you share the order code you want to cancel?", retry.PromptText);
        Assert.Null(session.Context.OrderCode);

        var confirmationPrompt = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("please cancel TA-9876 immediately", null),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Confirming, session.State);
        Assert.Equal("TA-9876", session.Context.OrderCode);
        Assert.Equal("TA-9876", session.Context.Metadata["order.code"]);
        Assert.Contains("Do you want me to cancel it now?", confirmationPrompt.PromptText);

        var cancelled = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("yes, cancel it", IntentLabels.Affirm),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Cancelled, session.State);
        Assert.True(cancelled.IsSessionComplete);
        Assert.Contains("TA-9876", cancelled.PromptText);
    }

    [Fact]
    public async Task HandleAsync_ModifyFlow_CollectsChangesAndConfirms()
    {
        var session = CreateSession();
        var initial = await _stateMachine.HandleAsync(session, CreateUtterance("I need to change my order", IntentLabels.ModifyOrder), CancellationToken.None);

        Assert.Equal(VoiceDialogState.Modifying, session.State);
        Assert.Equal("Tell me what needs to change in your order.", initial.PromptText);

        var update = await _stateMachine.HandleAsync(session, CreateUtterance("add extra sauce", IntentLabels.AddItem), CancellationToken.None);

        Assert.Contains("extra sauce", session.Context.RequestedItems);
        Assert.Equal("Anything else you'd like to change?", update.PromptText);

        var confirm = await _stateMachine.HandleAsync(session, CreateUtterance("that's all", IntentLabels.CompleteOrder), CancellationToken.None);

        Assert.Equal(VoiceDialogState.Confirming, session.State);
        Assert.Contains("Shall I finalize it?", confirm.PromptText);
        Assert.Equal("extra sauce", session.Context.Metadata["order.items"]);

        var affirmed = await _stateMachine.HandleAsync(session, CreateUtterance("go ahead", IntentLabels.Affirm), CancellationToken.None);

        Assert.Equal(VoiceDialogState.Completed, session.State);
        Assert.True(affirmed.IsSessionComplete);
        Assert.Contains("Your order is confirmed", affirmed.PromptText);
    }

    [Fact]
    public async Task HandleAsync_StatusFlow_TracksOrderAndCompletesOnAffirmation()
    {
        var session = CreateSession();
        var initial = await _stateMachine.HandleAsync(session, CreateUtterance("check on my order", IntentLabels.CheckStatus), CancellationToken.None);

        Assert.Equal(VoiceDialogState.CheckingStatus, session.State);
        Assert.Contains("what order code", initial.PromptText, StringComparison.OrdinalIgnoreCase);

        var lookup = await _stateMachine.HandleAsync(session, CreateUtterance("it's order TA-2468", null), CancellationToken.None);

        Assert.Equal(VoiceDialogState.CheckingStatus, session.State);
        Assert.Equal("TA-2468", session.Context.OrderCode);
        Assert.Equal("TA-2468", session.Context.Metadata["order.code"]);
        Assert.Contains("currently being prepared", lookup.PromptText);

        var completion = await _stateMachine.HandleAsync(session, CreateUtterance("yes that's it", IntentLabels.Affirm), CancellationToken.None);

        Assert.Equal(VoiceDialogState.Completed, session.State);
        Assert.True(completion.IsSessionComplete);
        Assert.Contains("ready for pickup", completion.PromptText);
    }

    [Fact]
    public async Task HandleAsync_FallbackIntent_TransitionsToErrorState()
    {
        var session = CreateSession();

        var result = await _stateMachine.HandleAsync(
            session,
            CreateUtterance("blargh", IntentLabels.Fallback),
            CancellationToken.None);

        Assert.Equal(VoiceDialogState.Error, session.State);
        Assert.Equal(VoiceDialogState.Error, result.State);
        Assert.Contains("not sure", result.PromptText);
    }

    private static VoiceDialogSession CreateSession() => new(Guid.NewGuid().ToString("N"));

    private async Task<VoiceDialogSession> CreateOrderingSessionAsync()
    {
        var session = CreateSession();
        await _stateMachine.HandleAsync(session, CreateUtterance("hello there", IntentLabels.Greeting), CancellationToken.None);
        return session;
    }

    private static VoiceDialogEvent CreateUtterance(string text, string? intentLabel, string confidence = "0.910")
    {
        IReadOnlyDictionary<string, string>? metadata = null;
        if (!string.IsNullOrWhiteSpace(intentLabel))
        {
            metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [IntentMetadataKeys.Label] = intentLabel,
                [IntentMetadataKeys.Confidence] = confidence
            };
        }

        return new VoiceDialogEvent(VoiceDialogEventType.Utterance, text, metadata);
    }
}
