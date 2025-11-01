# Voice order confirmation, pricing, and persistence

## Overview
The voice ordering flow now closes the loop from conversation to a persisted order. Once the dialog finite-state machine (FSM) detects that all required slots are filled (items and pickup time), it composes a structured summary, asks for explicit confirmation, and—when affirmed—creates an order in the transactional store using the existing ordering infrastructure. The response includes synthesized speech and rich metadata so downstream clients understand the confirmation status.

## Slot tracking and confirmation prompts
- `VoiceDialogContext` owns a `VoiceOrderDraft` that tracks normalized items, variants, modifiers, quantities, and the requested pickup time.
- `VoiceDialogStateMachine` parses each utterance to populate the draft:
  - Simple heuristics extract quantities, known modifiers (extra cheese, olives, etc.), and a handful of variant keywords.
  - Pickup times are recognized from expressions such as “pickup at 18:30” or “as soon as possible,” and stored as UTC slots.
- `RefreshOrderMetadata` keeps session metadata in sync (`order.items`, `order.item_count`, `order.pickup.*`). Once both items and pickup time are available, `HandleOrderCompletionCue` builds a human-friendly summary (for example, `2x Large Margherita with Extra Cheese for pickup at 18:30`) and transitions to the `Confirming` state with `order.confirmation = awaiting-user`.

## User confirmation and order finalization
- When the user affirms the confirmation prompt, `HandleConfirming` marks the session metadata with `order.finalize = true` and `order.confirmation = finalizing`, leaving the session in the confirming state while the backend performs persistence.
- The voice endpoint inspects this flag and calls `FinalizeOrderAsync` **before** saving the session:
  - The draft is validated to ensure required slots are still populated and the requested slot is not throttled by `IOrderThrottlingService`.
  - Available menu data are loaded once (`TakeawayDbContext.Products` including variants/modifiers). Mapping relies on the normalized keys produced during parsing to find the correct `Product`, `ProductVariant`, and `ProductModifier` records; unavailable items trigger a `VoiceOrderProcessingException` with user-facing guidance.
  - Pricing delegates to `IOrderPricingService` so totals match the rest of the system. Stock levels are decremented in the same way as the standard `/orders` endpoint.
  - The order is written inside a transaction, an order code is generated via `IOrderCodeGenerator`, and the usual audit log is recorded.
  - `IOrderStatusNotifier` and `IKitchenDisplayNotifier` broadcast the new order to the existing dashboards.
- On success the session transitions to `Completed`, the draft is cleared, and metadata captures the persisted order (`order.code`, `order.id`, `order.total`, `order.confirmation = persisted`). The FSM-produced summary is reused for the final prompt, which also mentions the total and pickup code.

## Audio response and metadata
- The prompt generated either by the FSM or by `FinalizeOrderAsync` is converted to speech through the existing `ITextToSpeechClient`. Clients receive the audio chunks plus structured metadata so they can display confirmation details, payment totals, and error reasons if needed.
- Metadata values follow a simple contract:
  - `order.confirmation` reflects state transitions (`collecting`, `awaiting-user`, `finalizing`, `persisted`, or `error`).
  - `order.summary`, `order.items`, `order.pickup.iso`, `order.pickup.display`, `order.total`, and `order.code` expose the structured order for UI surfaces.

## Failure handling
- Item/variant/modifier mismatches, slot throttling, or stock shortages raise `VoiceOrderProcessingException`. The endpoint logs the warning, resets `order.finalize` to `false`, sets `order.confirmation = collecting`, and returns to the ordering flow with an explanatory prompt.
- Database or other unexpected errors bubble into dedicated catch blocks, resulting in `order.confirmation = error`, a friendly apology prompt, and the session remaining open so the caller can try again.

## Summary of involved components
- **State machine (`VoiceDialogStateMachine`)** – Parses utterances, assembles the confirmation summary, manages dialog-state metadata, and flags when the backend should persist.
- **Voice endpoint (`VoiceEndpoints`)** – Executes the FSM, orchestrates order finalization with `TakeawayDbContext`, `IOrderPricingService`, throttling, code generation, and notifiers, and synthesizes the spoken response.
- **`VoiceOrderDraft`** – Central structure that accumulates items, variants, modifiers, quantities, and pickup information across turns, providing both metadata exports and final summaries.
- **`VoiceOrderProcessingException`** – Signals recoverable issues encountered while materializing the order so the caller receives actionable prompts without terminating the session.

Together these changes make the voice assistant capable of producing a real takeaway order that mirrors the REST workflows, while retaining the conversational context and surfacing comprehensive metadata to clients.
