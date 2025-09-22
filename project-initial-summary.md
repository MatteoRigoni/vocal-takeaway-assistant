# Vocal Takeaway Engine – Pizzeria da Asporto

## Product Goal
Reusable take-away ordering system (pizzeria as example) with **voice/text orders**, configurable menu, pickup scheduling with throttling, payment simulation, and integrated **Kitchen Display System (KDS)**. Distributed as **one-liner installer (Docker Compose)** or offline bundle.

## Target Users
- Small restaurants (pizzerias, sushi, kebab, etc.)  
- Staff roles: Admin, Cashier, Kitchen  
- Customers: web or voice-based ordering

## Chosen Stack & Architecture
- **Backend:** ASP.NET Core 8 WebAPI, EF Core (SQLite, Postgres optional), Quartz.NET, FluentValidation, Serilog JSON  
- **Frontend:** Angular 17, SignalR client, Tailwind/Bootstrap, WebAudio API  
- **Realtime:** SignalR over WebSockets  
- **STT/TTS:** faster-whisper server + piper server (containers)  
- **Infra:** Docker Compose, Caddy/Traefik (TLS), HealthChecks UI, prometheus-net  
- **Optional AI:** ML.NET intent classifier (Order/Modify/Cancel/Status/SmallTalk)

## Main Features
- Menu browsing & cart (products, variants, modifiers)  
- Voice order (FSM + optional ML.NET intents) with confirmation  
- Order lifecycle (place, modify, cancel, track status)  
- KDS board with real-time updates & touch-friendly UI  
- Throttling per 15-min slot (avoid kitchen overload)  
- Security: JWT + RBAC (Admin, Cashier, Kitchen, ReadOnly), TLS in shop profile  
- Data export (CSV), encrypted backups, immutable audit logs  
- Offline/online installation scripts  

## Non-Functional Requirements
- End-to-end latency: <900ms (voice order flow)  
- Secure: TLS, JWT, audit logs with PII-masking  
- Observability: Serilog JSON logs, Prometheus metrics, Grafana dashboards  
- One-liner install & offline bundle with pre-pulled Docker images  
- Configurable profiles: **demo** (simple auth, rich seed) and **shop** (full security, throttling, backups)  

## Trade-offs & Assumptions
- **SQLite** chosen for MVP (lightweight, offline-ready); Postgres kept as future option  
- **STT/TTS local containers** (no paid APIs) – higher CPU/GPU demand  
- FSM + ML.NET light intents for voice → advanced features (barge-in, upsell) included but optional stretch goals  
- Distribution focused on Docker Compose; native .NET installer postponed  
