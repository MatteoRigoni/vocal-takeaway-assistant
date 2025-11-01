Title: REPO-001: Initialize mono-repo with .NET + Angular #1 
**Summary**: Create solution with ASP.NET Core backend and Angular 17 frontend inside a 
mono-repo with Docker support. 
**Acceptance Criteria**: - Given a fresh clone, when the developer runs `docker compose up`, then both backend 
and frontend containers start with hello-world endpoints. 
**Tasks**: - Setup Git repo with root README and license - Scaffold ASP.NET Core 8 WebAPI project - Scaffold Angular 17 workspace with Tailwind - Add Dockerfiles (API, frontend) and docker-compose.yml skeleton - Configure Caddy/Traefik as reverse proxy with TLS - Add `.env` configuration for branding/shop info - Smoke test with `/health` endpoint 
**Effort**: Medium --- 
Title: DB-001: Implement EF Core domain model & migrations #2 
**Summary**: Define entities, relationships, and migrations for all domain entities (Shop, 
Product, Order, etc.) with SQLite backend. 
**Acceptance Criteria**: - Given EF migration applied, when DB is created, then all entities exist with correct 
relationships and constraints. 
**Tasks**: - Define entity classes and DbContext - Configure EF Core migrations with SQLite provider - Seed demo menu with sample products (Margherita, Diavola, Coca-Cola) - Add branding config seed (shop name, logo placeholder) - Add unit tests for seeding 
**Effort**: Medium --- 
Title: API-001: Implement /menu and /orders endpoints #3 
**Summary**: Provide API for menu browsing and order creation with validation and 
throttling. 
**Acceptance Criteria**: 
- Given a valid request, when POST /orders is called, then order is persisted, price is 
calculated, and OrderCode returned. 
**Tasks**: - Implement DTOs for orders and menu - Add FluentValidation rules (qty > 0, product active, stock > 0) - Implement pricing calculation (variants, modifiers, rounding, VAT) - Implement throttling logic per 15-min slot - Add audit log entry for each order - Add seed demo endpoint `/api/demo/reset` to reload demo menu/orders - Integration tests for pricing + throttling 
**Effort**: Large --- 
Title: API-002: Implement order lifecycle endpoints #4 
**Summary**: Add endpoints for status, modify, cancel, and payment simulation. 
**Acceptance Criteria**: - Given an order in pending, when PATCH /orders/{id} sets status=ready, then KDS board 
updates in real-time. 
**Tasks**: - GET /orders/{code} for status - PATCH /orders/{id} (status, pickupAt, notes) - POST /orders/{id}/pay (cash/card/online-sim) - Cancel order logic (time-based policy) - Add CSV export endpoint `/api/orders/export` - Unit + integration tests 
**Effort**: Medium --- 
Title: FEAT-001: Implement SignalR hubs and KDS UI #5 
**Summary**: Add SignalR hubs for order notifications and build Angular KDS dashboard. 
**Acceptance Criteria**: - Given a new order, when placed, then it appears on KDS in under 1s with pending status. 
**Tasks**: - Create SignalR hub (OrdersHub, KDSHub) - Implement Angular service + KDS board component - Show order tickets with timers, filters, and sound on ready - Add shop logo/branding at top of KDS board 
- Touch-friendly layout with auto-refresh - E2E test with backend + frontend 
**Effort**: Large --- 
Title: FEAT-002: Integrate STT/TTS containers #6 
**Summary**: Connect backend with faster-whisper (STT) and piper (TTS) servers for voice 
interaction. 
**Acceptance Criteria**: - Given microphone input, when user says "One Margherita", then STT returns recognized 
text and TTS confirms. 
**Tasks**: - Setup docker-compose services for STT/TTS - Implement STT client wrapper in backend - Implement TTS client wrapper in backend - Angular voice component with WebAudio input/output - Add toggle for "Voice mode" vs "Text mode" - Test with demo voice order 
**Effort**: Large --- 
Title: FEAT-003: Implement FSM + ML.NET intent classifier #7 
**Summary**: Implement voice dialog manager with FSM + optional ML.NET intents. 
**Acceptance Criteria**: - Given an order conversation, when user completes slots, then FSM confirms and creates 
order. 
**Tasks**: - Implement FSM states for order/modify/cancel/status - Add ML.NET lightweight classifier for intents - Handle slot filling: product, variant, qty, modifiers, pickup time - Add voice confirmation summary before persisting order - Optional: upsell suggestion if total < threshold - Add demo script config for a sample conversation - Unit tests for FSM transitions 
**Effort**: Large --- 
Title: FEAT-004: Add barge-in support for voice orders #8 
**Summary**: Allow user to interrupt TTS with new speech input. 
**Acceptance Criteria**: - Given ongoing TTS, when user starts speaking, then playback stops and new input is 
processed. 
**Tasks**: - Implement streaming TTS with cancel support - Detect microphone input mid-playback - Update FSM state on interruption - Test with demo scenario 
**Effort**: Medium --- 
Title: SEC-001: Implement JWT auth with RBAC #9 
**Summary**: Secure all APIs with JWT and roles (Admin, Cashier, Kitchen, ReadOnly). 
**Acceptance Criteria**: - Given a Cashier token, when accessing /kds/tickets, then access is denied with 403. 
**Tasks**: - Add JWT authentication middleware - Define roles and policies per endpoint - Implement login endpoint (demo seed users) - Add “quick login” option in demo profile - Unit + integration tests 
**Effort**: Medium --- 
Title: SEC-002: Add TLS, audit logs, and rate limiting #10 
**Summary**: Harden system with TLS, append-only audit logs, and per-IP rate limit on 
orders. 
**Acceptance Criteria**: - Given rate limit configured, when same IP sends >N orders/min, then 429 is returned. 
**Tasks**: - Configure Caddy/Traefik TLS + HSTS - Implement audit log repository (append-only) 
- Mask PII fields in logs - Add ASP.NET rate limiter on POST /orders - HealthChecks (/health, /ready) + Prometheus metrics - Grafana dashboard setup - Daily backup cron job for demo profile 
**Effort**: Large --- 
Title: OPS-001: Create online/offline installation scripts #11 
**Summary**: Provide installation/uninstallation scripts with profiles demo/shop. 
**Acceptance Criteria**: - Given `./install.sh --profile demo`, when executed, then system starts with seeded demo 
data. 
**Tasks**: - install.sh for online mode (compose pull + up) - install-offline.sh using pre-pulled images - uninstall.sh with backup step - Backup/restore scripts for DB - Verify cross-platform (Linux/Mac/Win WSL) - Add profile flag `--shop` vs `--demo` with different configs 
**Effort**: Medium --- 
Title: DOC-001: Write README and Go-Live docs #12 
**Summary**: Document architecture, installation, usage, and go-live checklist with diagrams 
and screenshots. 
**Acceptance Criteria**: - Given project repo, when user opens README.md, then they see install instructions, demo 
guide, and architecture diagram. 
**Tasks**: - Add Mermaid diagram for architecture - Add screenshots/gif of UI - Write online/offline install instructions - Provide demo script for voice + KDS flow - Add section on customizing branding/menu - Export orders CSV daily cron job - Checklist for production readiness 
**Effort**: Medium --- 
Title: DOC-002: Build demo portfolio for local companies #13 
**Summary**: Prepare a professional demo package (slides, video, scripts) to present 
system to local restaurants for cold calls and trials. 
**Acceptance Criteria**: - Given demo package, when presented to a restaurant owner, then they understand system 
value, see example flow, and know how to test it. 
**Tasks**: - Create short slide deck (problem, solution, value props, screenshots) - Record 2–3min screen capture demo (placing order, KDS update, voice order) - Prepare cold-call script with system intro and benefits - Package demo scripts (voice, modify, cancel order) - Provide “demo reset” instructions for quick replays - Export demo bundle (slides, video, script, installer) 
**Effort**: Medium 