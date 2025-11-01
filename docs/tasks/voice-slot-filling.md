# Voice Slot Filling

The voice ordering dialog tracks a structured set of slots to drive the order-taking flow. Slots are persisted inside each voice session and surfaced through the `/voice/session` API so clients can render the current status.

## Slot definitions

| Slot | Description | Validation |
| --- | --- | --- |
| **Product** | Menu item chosen by the caller. Stored as product id and display name. | Product must exist in the active menu. Selecting a new product clears dependent slots (variant, modifiers, pickup time). |
| **Variant** | Specific variant of the chosen product (e.g., size). | Must belong to the selected product. If the product exposes a single variant, it is selected automatically. |
| **Quantity** | Number of items requested. | Accepts digits or number words (`one`-`ten`). Values must be between 1 and 50 (`SlotValidation.MaxQuantity`). |
| **Modifiers** | Optional extras tied to the product. Tracks the selected modifier ids and names plus an explicit "no modifiers" flag. | Only modifiers available for the product can be selected. Users can also decline modifiers (“no extras”). |
| **Pickup time** | Desired pickup timestamp. Normalized to a `DateTimeOffset`. | Parsed with current culture fallback to invariant culture. Time must be at least 10 minutes in the future (`SlotValidation.IsValidPickupTime`). Times earlier than “now” roll over to the next day. |

`VoiceOrderSlots` exposes helper methods for clearing, applying snapshots from the API contract, and projecting to `VoiceOrderSlotsSnapshot` for responses.

## Prompt and state progression

`VoiceDialogStateMachine.HandleOrderingAsync` orchestrates slot filling:

1. **Intent routing** – incoming utterances still honor global intents (status checks, cancellations, modifications). These transitions happen before slot processing.
2. **Slot capture** – `ApplyUtteranceToSlots` normalizes the utterance:
   - Products are matched by name (case-insensitive) against the live menu loaded from EF Core (`LoadMenuSnapshotAsync`).
   - Variants and modifiers use both literal phrase matching and normalized token matching.
   - Quantities parse digits or number words. Pickup times rely on `DateTime.TryParse` with the `IDateTimeProvider` to enforce lead time.
   - Negations like “no toppings” mark modifiers as explicitly empty.
3. **Prompt selection** – After each utterance the FSM checks the slots in order:
   1. Product missing → “What would you like to order? …” (includes popular suggestions).
   2. Variant missing (when multiple exist) → “Which variant of {product} …”.
   3. Quantity missing → “How many … should I prepare?”
   4. Modifiers missing (and available) → “Would you like any modifiers …”.
   5. Pickup time missing → “When should it be ready for pickup?”
4. **Confirmation** – When all slots are filled the dialog transitions to `VoiceDialogState.Confirming`, summarising the order (quantity, variant, modifiers, pickup window) and storing a human-readable string in `metadata["order.items"]`.

Slot metadata keys are also published for clients (`slot.product.id`, `slot.quantity`, etc.) whenever `BuildResult` emits a response.

## Normalisation details

* **Menu lookup** – Each request loads available products, variants, and modifiers from `TakeawayDbContext` using `AsNoTracking` and stores a lightweight snapshot. This avoids keeping DbContexts inside the singleton state machine and lets us reuse menu information across slot updates.
* **Text parsing** – `NormalizeForLookup` removes punctuation so phrases like “Coca Cola” match the seeded “Coca-Cola” product. Quantities accept both numbers and words, while pickup times roll forward if the interpreted time is in the past.
* **Snapshot interchange** – API contracts (`VoiceOrderSlotsDto`) are mapped from/to internal snapshots via `VoiceOrderSlotMapper`. Clients can send the previous snapshot in the next request to resync state, and every response includes the latest slot snapshot.

## Example conversations

### Complete order

| Turn | Speaker | Utterance | Effect |
| --- | --- | --- | --- |
| 1 | User | “I’d like two Margheritas.” | Product set to *Margherita*, quantity parsed as 2. Variant defaults to the only variant. FSM prompts for modifiers. |
| 2 | Assistant | “Would you like any modifiers for Margherita? Available: Extra Cheese, Olives.” | — |
| 3 | User | “Add extra cheese and it’ll be ready at 7 pm.” | Modifier *Extra Cheese* selected; pickup time parsed as today 19:00 (rolls to next day if already past). |
| 4 | Assistant | “Great, 2 x Margherita (with Extra Cheese), ready at 19:00. Shall I place the order?” | All slots filled → moves to confirming. |
| 5 | User | “Yes, place it.” | FSM finalises the order, generates pickup code, session completes and slots reset. |

### Partial order with missing pickup time

| Turn | Speaker | Utterance | Effect |
| --- | --- | --- | --- |
| 1 | User | “Give me a large Diavola.” | Product matched, variant resolved to *Large*, quantity defaults to pending, prompt asks for quantity. |
| 2 | User | “Make it three.” | Quantity slot filled. Next prompt asks about modifiers. |
| 3 | User | “No extras.” | Modifiers marked as explicitly none. FSM now asks: “When should it be ready for pickup?” |
| 4 | User | *(silence)* | Timeout handler reminds the caller to provide a pickup time. Slots remain intact until the next answer. |

These examples mirror the behaviour encoded in `VoiceDialogStateMachine.HandleOrderingAsync` and help QA teams verify the Angular test harness after backend changes.

## Client integration notes

* The Angular voice harness (`frontend/src/app/voice`) persists the `VoiceOrderSlotsDto` snapshot returned by each response and sends it back with the next `VoiceSessionRequest`. This mirrors how production clients should maintain continuity between microphone turns.
* Slot fill status drives the progress UI. When the backend marks a slot as filled (`IsFilled == true`), the client highlights that step and prepares the next prompt bubble.
* If the assistant clears dependent slots (for example, changing the product resets variants and modifiers), the UI responds automatically because the snapshot replaces the previous state.
