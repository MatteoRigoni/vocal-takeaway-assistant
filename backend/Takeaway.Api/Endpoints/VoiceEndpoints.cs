using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Takeaway.Api.Data;
using Takeaway.Api.Domain.Constants;
using Takeaway.Api.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Takeaway.Api.Authorization;
using Takeaway.Api.Contracts.Voice;
using Takeaway.Api.Extensions;
using Takeaway.Api.Services;
using Takeaway.Api.Validation;
using Takeaway.Api.VoiceDialog;
using Takeaway.Api.VoiceDialog.IntentClassification;

namespace Takeaway.Api.Endpoints;

public static class VoiceEndpoints
{
    public static IEndpointRouteBuilder MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/voice")
            .RequireAuthorization(AuthorizationPolicies.VoiceAutomation);

        group.MapPost("/session",
            async Task<Results<Ok<VoiceSessionResponse>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>>(
                VoiceSessionRequest request,
                IValidator<VoiceSessionRequest> validator,
                ISpeechToTextClient speechToTextClient,
                ITextToSpeechClient textToSpeechClient,
                IVoiceDialogSessionStore sessionStore,
                IVoiceDialogStateMachine stateMachine,
                IIntentClassifier intentClassifier,
                TakeawayDbContext dbContext,
                IOrderPricingService pricingService,
                IOrderThrottlingService throttlingService,
                IDateTimeProvider clock,
                IOrderCodeGenerator codeGenerator,
                IOrderStatusNotifier statusNotifier,
                IKitchenDisplayNotifier kitchenNotifier,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken
            ) =>
            {
                var logger = loggerFactory.CreateLogger("VoiceSession");

                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                {
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());
                }

                var audioChunks = request.AudioChunks ?? Array.Empty<string>();
                List<byte[]> audio;
                try
                {
                    audio = DecodeChunks(audioChunks);
                }
                catch (FormatException ex)
                {
                    return TypedResults.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        [nameof(request.AudioChunks)] = new[] { ex.Message }
                    }));
                }

                var recognizedText = request.UtteranceText?.Trim() ?? string.Empty;
                if (audio.Count > 0)
                {
                    try
                    {
                        var transcription = await speechToTextClient.TranscribeAsync(ToStream(audio), cancellationToken);
                        recognizedText = transcription.Text;
                    }
                    catch (SpeechClientException ex)
                    {
                        return ToProblemResult("Speech-to-text service error", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        return TypedResults.Problem(title: "Speech-to-text service unreachable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                var session = await sessionStore.GetOrCreateAsync(request.CallerId, cancellationToken);
                IntentPrediction intentPrediction = intentClassifier.PredictIntent(recognizedText);
                Dictionary<string, string>? eventMetadata = null;
                if (intentPrediction.HasPrediction)
                {
                    eventMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [IntentMetadataKeys.Label] = intentPrediction.Label!,
                        [IntentMetadataKeys.Confidence] = intentPrediction.Confidence.ToString("0.000", CultureInfo.InvariantCulture)
                    };
                }

                VoiceDialogResult dialogResult;
                try
                {
                    dialogResult = await stateMachine.HandleAsync(
                        session,
                        new VoiceDialogEvent(VoiceDialogEventType.Utterance, recognizedText, eventMetadata),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Dialog state machine failed for session {SessionId}.", session.Id);
                    var prompt = "Something went wrong while processing your request. Please try again.";
                    return TypedResults.Ok(new VoiceSessionResponse(
                        recognizedText,
                        Array.Empty<string>(),
                        prompt,
                        VoiceDialogState.Error.ToString(),
                        true,
                        new Dictionary<string, string> { ["error"] = "dialog-failure" }
                    ));
                }

                var metadata = session.Context.Metadata;
                var shouldFinalize = metadata.TryGetValue("order.finalize", out var finalizeValue)
                    && string.Equals(finalizeValue, "true", StringComparison.OrdinalIgnoreCase);

                if (shouldFinalize)
                {
                    try
                    {
                        dialogResult = await FinalizeOrderAsync(
                            session,
                            dbContext,
                            pricingService,
                            throttlingService,
                            clock,
                            codeGenerator,
                            statusNotifier,
                            kitchenNotifier,
                            logger,
                            cancellationToken);
                    }
                    catch (VoiceOrderProcessingException ex)
                    {
                        logger.LogWarning(ex, "Voice order processing failed for session {SessionId}", session.Id);
                        metadata["order.finalize"] = "false";
                        metadata["order.confirmation"] = "collecting";
                        var prompt = ex.Message;
                        session.TransitionTo(VoiceDialogState.Ordering);
                        session.Context.LastPrompt = prompt;
                        dialogResult = new VoiceDialogResult(session.State, prompt, false, metadata);
                    }
                    catch (DbUpdateException ex)
                    {
                        logger.LogError(ex, "Database error while finalizing voice order for session {SessionId}", session.Id);
                        metadata["order.finalize"] = "false";
                        metadata["order.confirmation"] = "error";
                        var prompt = "I couldn't place the order because of a system problem. Let's try again.";
                        session.TransitionTo(VoiceDialogState.Error);
                        session.Context.LastPrompt = prompt;
                        dialogResult = new VoiceDialogResult(session.State, prompt, false, metadata);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Unexpected error while finalizing voice order for session {SessionId}", session.Id);
                        metadata["order.finalize"] = "false";
                        metadata["order.confirmation"] = "error";
                        var prompt = "Something went wrong while placing the order. Should we try again?";
                        session.TransitionTo(VoiceDialogState.Error);
                        session.Context.LastPrompt = prompt;
                        dialogResult = new VoiceDialogResult(session.State, prompt, false, metadata);
                    }
                }

                await sessionStore.SaveAsync(session, cancellationToken);

                if (dialogResult.IsSessionComplete)
                {
                    await sessionStore.ClearAsync(session.Id, cancellationToken);
                }

                var responseAudio = new List<string>();
                if (!string.IsNullOrWhiteSpace(dialogResult.PromptText))
                {
                    try
                    {
                        await foreach (var chunk in textToSpeechClient.SynthesizeAsync(new TextToSpeechRequest(dialogResult.PromptText, request.Voice), cancellationToken))
                        {
                            responseAudio.Add(Convert.ToBase64String(chunk));
                        }
                    }
                    catch (SpeechClientException ex)
                    {
                        return ToProblemResult("Text-to-speech service error", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        return TypedResults.Problem(title: "Text-to-speech service unreachable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                return TypedResults.Ok(new VoiceSessionResponse(
                    recognizedText,
                    responseAudio,
                    dialogResult.PromptText,
                    dialogResult.State.ToString(),
                    dialogResult.IsSessionComplete,
                    dialogResult.Metadata
                ));
            })
        .WithName("CreateVoiceSession")
        .WithSummary("Transcribe incoming audio, orchestrate the dialog flow, and synthesize the response.");
        return app;
    }

    private static List<byte[]> DecodeChunks(IReadOnlyList<string> audioChunks)
    {
        var decoded = new List<byte[]>(audioChunks.Count);
        foreach (var chunk in audioChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            decoded.Add(Convert.FromBase64String(chunk));
        }

        return decoded;
    }

    private static async IAsyncEnumerable<byte[]> ToStream(IEnumerable<byte[]> audioChunks)
    {
        foreach (var chunk in audioChunks)
        {
            if (chunk is null || chunk.Length == 0)
            {
                continue;
            }

            yield return chunk;
            await Task.Yield();
        }
    }

    private static ProblemHttpResult ToProblemResult(string title, SpeechClientException exception)
    {
        var status = exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : StatusCodes.Status502BadGateway;
        var detail = !string.IsNullOrWhiteSpace(exception.ResponseBody) ? exception.ResponseBody : exception.Message;
        return TypedResults.Problem(title: title, detail: detail, statusCode: status);
    }

    private static async Task<VoiceDialogResult> FinalizeOrderAsync(
        VoiceDialogSession session,
        TakeawayDbContext dbContext,
        IOrderPricingService pricingService,
        IOrderThrottlingService throttlingService,
        IDateTimeProvider clock,
        IOrderCodeGenerator codeGenerator,
        IOrderStatusNotifier statusNotifier,
        IKitchenDisplayNotifier kitchenNotifier,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var metadata = session.Context.Metadata;
        metadata["order.finalize"] = "false";

        var draft = session.Context.OrderDraft;
        if (!draft.AreRequiredSlotsFilled)
        {
            var promptMissing = "I still need a pickup time before placing the order. When should it be ready?";
            session.TransitionTo(VoiceDialogState.Ordering);
            session.Context.LastPrompt = promptMissing;
            metadata["order.confirmation"] = "collecting";
            return new VoiceDialogResult(session.State, promptMissing, false, metadata);
        }

        var pickupUtc = draft.PickupTime!.Value.UtcDateTime;
        var slotStart = NormalizeSlot(pickupUtc);

        if (!await throttlingService.CanPlaceOrderAsync(slotStart, cancellationToken))
        {
            var promptThrottled = "That pickup slot is fully booked. Please choose another time.";
            session.TransitionTo(VoiceDialogState.Ordering);
            session.Context.LastPrompt = promptThrottled;
            metadata["order.confirmation"] = "collecting";
            return new VoiceDialogResult(session.State, promptThrottled, false, metadata);
        }

        var summary = metadata.TryGetValue("order.summary", out var storedSummary)
            ? storedSummary
            : draft.BuildSummary(CultureInfo.CurrentCulture);

        var receivedStatus = await dbContext.OrderStatuses
            .FirstAsync(s => s.Name == OrderStatusCatalog.Received, cancellationToken);

        var products = await dbContext.Products
            .Include(p => p.Variants)
            .Include(p => p.Modifiers)
            .Where(p => p.ShopId == draft.ShopId)
            .ToListAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = new Order
        {
            ShopId = draft.ShopId,
            OrderChannelId = draft.OrderChannelId,
            OrderStatusId = receivedStatus.Id,
            OrderStatus = receivedStatus,
            OrderDate = slotStart,
            CreatedAt = clock.UtcNow,
            DeliveryAddress = string.Empty,
            Notes = metadata.TryGetValue("order.notes", out var notes) ? notes : null,
            TotalAmount = 0m
        };

        foreach (var itemDraft in draft.Items)
        {
            var product = ResolveProduct(products, itemDraft)
                ?? throw new VoiceOrderProcessingException("product-not-found", $"I couldn't find {itemDraft.ProductName} on the menu.");

            if (!product.IsAvailable)
            {
                throw new VoiceOrderProcessingException("product-unavailable", $"{product.Name} is not available right now.");
            }

            var variant = ResolveVariant(product, itemDraft);
            if (itemDraft.VariantName is not null && variant is null)
            {
                throw new VoiceOrderProcessingException("variant-not-found", $"I couldn't find the {itemDraft.VariantName} option for {product.Name}.");
            }

            var modifiers = ResolveModifiers(product, itemDraft);

            var availableStock = variant?.StockQuantity ?? product.StockQuantity;
            if (availableStock < itemDraft.Quantity)
            {
                throw new VoiceOrderProcessingException("stock-unavailable", $"We only have {availableStock} {product.Name} left for today.");
            }

            var pricing = pricingService.Calculate(product, variant, modifiers, itemDraft.Quantity);
            order.TotalAmount += pricing.Subtotal;

            var appliedModifierNames = modifiers.Select(m => m.Name).ToArray();

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductVariantId = variant?.Id,
                VariantName = variant?.Name,
                Quantity = itemDraft.Quantity,
                UnitPrice = pricing.UnitPrice,
                Subtotal = pricing.Subtotal,
                Modifiers = appliedModifierNames.Length > 0 ? JsonSerializer.Serialize(appliedModifierNames) : null,
                Product = product
            });

            if (variant is not null)
            {
                variant.StockQuantity -= itemDraft.Quantity;
            }
            else
            {
                product.StockQuantity -= itemDraft.Quantity;
            }
        }

        if (order.TotalAmount <= 0)
        {
            throw new VoiceOrderProcessingException("pricing-failed", "I couldn't calculate the total for this order.");
        }

        dbContext.Orders.Add(order);

        await dbContext.SaveChangesAsync(cancellationToken);

        order.OrderCode = codeGenerator.Generate(slotStart, order.Id);

        dbContext.AuditLogs.Add(new AuditLog
        {
            OrderId = order.Id,
            EventType = "OrderCreated",
            CreatedAt = clock.UtcNow,
            Payload = JsonSerializer.Serialize(new
            {
                order.OrderCode,
                order.TotalAmount,
                Items = order.Items.Select(i => new
                {
                    i.ProductId,
                    i.Quantity,
                    i.ProductVariantId,
                    i.UnitPrice,
                    i.Subtotal
                })
            })
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await statusNotifier.NotifyOrderCreatedAsync(order, cancellationToken);
        await kitchenNotifier.NotifyTicketCreatedAsync(order, cancellationToken);

        session.TransitionTo(VoiceDialogState.Completed);
        session.Context.OrderCode = order.OrderCode;
        metadata["order.code"] = order.OrderCode;
        metadata["order.total"] = order.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture);
        metadata["order.id"] = order.Id.ToString(CultureInfo.InvariantCulture);
        metadata["order.confirmation"] = "persisted";
        metadata["order.pickup.iso"] = order.OrderDate.ToString("O", CultureInfo.InvariantCulture);
        metadata["order.pickup.display"] = order.OrderDate.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        var finalSummary = !string.IsNullOrWhiteSpace(summary)
            ? summary
            : draft.BuildSummary(CultureInfo.CurrentCulture);
        if (!string.IsNullOrWhiteSpace(finalSummary))
        {
            metadata["order.summary"] = finalSummary;
        }

        var totalText = order.TotalAmount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        var prompt = !string.IsNullOrWhiteSpace(finalSummary)
            ? $"Order confirmed: {finalSummary}. The total is {totalText}. Your pickup code is {order.OrderCode}."
            : $"Order confirmed. The total is {totalText}. Your pickup code is {order.OrderCode}.";

        session.Context.LastPrompt = prompt;
        draft.Clear();

        return new VoiceDialogResult(session.State, prompt, true, metadata);
    }

    private static Product? ResolveProduct(IEnumerable<Product> products, VoiceOrderItemDraft itemDraft)
    {
        var normalized = itemDraft.NormalizedProductName;
        foreach (var product in products)
        {
            var productKey = VoiceOrderItemDraft.NormalizeKey(product.Name);
            if (string.Equals(productKey, normalized, StringComparison.Ordinal)
                || productKey.Contains(normalized, StringComparison.Ordinal)
                || normalized.Contains(productKey, StringComparison.Ordinal))
            {
                return product;
            }
        }

        return null;
    }

    private static ProductVariant? ResolveVariant(Product product, VoiceOrderItemDraft itemDraft)
    {
        if (string.IsNullOrWhiteSpace(itemDraft.VariantName))
        {
            return product.Variants.FirstOrDefault(v => v.IsDefault);
        }

        var normalizedVariant = VoiceOrderItemDraft.NormalizeKey(itemDraft.VariantName);
        return product.Variants.FirstOrDefault(v =>
            string.Equals(VoiceOrderItemDraft.NormalizeKey(v.Name), normalizedVariant, StringComparison.Ordinal));
    }

    private static List<ProductModifier> ResolveModifiers(Product product, VoiceOrderItemDraft itemDraft)
    {
        if (itemDraft.Modifiers.Count == 0)
        {
            return new List<ProductModifier>();
        }

        var modifiers = new List<ProductModifier>();
        foreach (var modifierName in itemDraft.Modifiers)
        {
            var normalized = VoiceOrderItemDraft.NormalizeKey(modifierName);
            var match = product.Modifiers.FirstOrDefault(m =>
                string.Equals(VoiceOrderItemDraft.NormalizeKey(m.Name), normalized, StringComparison.Ordinal));

            if (match is null)
            {
                throw new VoiceOrderProcessingException("modifier-not-found", $"I couldn't find the {modifierName} option for {product.Name}.");
            }

            modifiers.Add(match);
        }

        return modifiers;
    }

    private static DateTime NormalizeSlot(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        }

        var slotMinutes = (timestampUtc.Minute / 15) * 15;
        return new DateTime(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day, timestampUtc.Hour, slotMinutes, 0, DateTimeKind.Utc);
    }
}
}
