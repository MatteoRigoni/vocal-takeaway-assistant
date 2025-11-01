# Voice dialog state machine tests

## Scenarios covered
- Start intent transitions into ordering with classifier-provided metadata captured in the session context.
- Ordering flow adds cleaned menu items, produces upsell prompts, and transitions through confirmation to completion on affirmation.
- Cancellation flow validates order code slots, retries on invalid input, confirms before cancellation, and closes the session once affirmed.
- Modification flow collects change requests, summarizes them for confirmation, and finalizes when the user agrees.
- Status checks capture order codes, provide in-progress updates, and complete after user confirmation.
- Fallback intent moves the dialog to the error state with a recovery prompt.

## Test fixtures and helpers
- `CreateOrderingSessionAsync` seeds a session with a mocked greeting intent to exercise downstream ordering behavior without repeating setup.
- `CreateUtterance` simulates the intent classifier output by attaching intent labels and confidence values to dialog events, letting tests focus on slot updates and prompts.

## Coverage gaps / follow-ups
- Does not validate synthesized audio responses or integration with `VoiceEndpoints`.
- Does not cover timeout/system events or negative confirmation branches beyond cancellation.
- Order code generation is asserted for presence but not format stability because the value is time-dependent.
