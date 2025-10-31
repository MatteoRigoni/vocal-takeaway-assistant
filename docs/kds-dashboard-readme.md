# Kitchen Display System (KDS) Dashboard

This release adds a real-time kitchen display system that keeps restaurant teams in sync with takeaway orders. The change introduces SignalR hubs on the backend and a dedicated Angular dashboard for the kitchen staff.

## Business Overview

- **Live ticket feed:** Orders appear on the kitchen board in under one second with the initial `Received` status so chefs can begin preparation immediately.
- **Status tracking:** Tickets track status transitions (`Received`, `InPreparation`, `Ready`) and drop off the board automatically once they are completed or cancelled to keep the workspace tidy.
- **Operational context:** Each ticket shows customer details, modifiers, collection time and running timers so staff can prioritise at a glance.
- **Touch friendly controls:** Large hit targets, high contrast colours and real-time refresh make the UI suitable for wall-mounted tablets.
- **Actionable tickets:** Kitchen staff can progress orders through `Start prep`, `Mark ready` and `Complete` actions without leaving the board.
- **Audible ready alert:** When a ticket transitions to `Ready` the board emits a soft tone so counter staff know a pickup is waiting.

## Architecture Changes

### SignalR hubs

Two hubs broadcast order events:

- `OrdersHub` continues to serve customer-facing status updates and now also publishes an `OrderCreated` event.
- `KdsHub` is dedicated to the kitchen display. It emits:
  - `TicketCreated` when a new order enters the queue.
  - `TicketUpdated` whenever notes, pickup time or status change.
  - `TicketRemoved` when an order leaves the active kitchen flow (completed/cancelled).

The hubs reuse EF Core entities and `OrderMappingExtensions` to serialise kitchen-friendly DTOs that include customer, item, modifier and pricing data.

### API surface

`/orders/kds` returns the current active kitchen queue. Optional query parameters allow filtering by status. Endpoints that create or update orders dispatch both the customer notifications and the KDS events through the new `IKitchenDisplayNotifier`.

### Angular dashboard

The Angular app now routes `/kds` to a standalone `KdsBoardComponent` backed by `KitchenDisplayService`:

- Connects to `/hubs/kds` with automatic reconnects and replays a fresh snapshot after reconnects.
- Maintains a signal-based store of tickets, exposes filters (`All`, `Pending`, `Cooking`, `Ready`) and periodically refreshes via REST as a safety net.
- Provides contextual action buttons so chefs can move tickets between `Received`, `InPreparation`, `Ready` and `Completed` states. Buttons disable while updates are in-flight to avoid duplicate submissions.
- Provides derived state for timers, connection badges, counts and relative timestamps.
- Uses Web Audio API to emit an audible cue when tickets flip to `Ready`.

Styling is fully responsive with a dark, high-contrast theme optimised for touch devices. Timers use a monospace font to improve legibility from a distance.

## Testing Strategy

- **Backend integration:** `KitchenDisplayTests` spins up the ASP.NET test server, connects with a real `HubConnection`, posts an order and asserts that the KDS hub broadcasts within one second. It also validates the `/orders/kds` endpoint shape.
- **Angular unit tests:** `kitchen-display.service.spec.ts` verifies that the service ingests snapshots and honours filter selections without requiring a live hub.

## Running the Stack

1. Apply .NET migrations and run tests: `dotnet test backend/Takeaway.Api.Tests/Takeaway.Api.Tests.csproj`.
2. Install frontend dependencies (requires internet access to fetch `@microsoft/signalr`) and execute Angular tests: `npm install && npm test` inside `frontend/`.
3. Start services with Docker Compose or run projects individually. When everything is running, visit `/kds` on the Angular app to see the live dashboard.

## Implementation Notes

- The backend normalises order statuses before broadcasting to ensure the UI receives consistent casing.
- Snapshot DTOs collapse order item modifier JSON into arrays for immediate display.
- Auto-refresh is set to 30 seconds; hubs keep the board reactive while REST refreshes guard against missed events.
- Connection loss is surfaced to the user with a badge and descriptive text. Reconnect attempts resume automatically every two seconds until successful.

For a quick tour of the UI look at `frontend/src/app/kds/kds-board.component.html` and related CSS. Backend mapping logic lives in `backend/Takeaway.Api/Extensions/OrderMappingExtensions.cs`.
