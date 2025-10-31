# vocal-takeaway-assistant

Reusable take-away ordering system with voice/text orders.

## Backend domain

The domain entities, relationships and seed data are documented in [docs/domain-model.md](docs/domain-model.md).

## Kitchen display dashboard

The real-time kitchen display board powered by SignalR hubs and the Angular KDS client is documented in [docs/kds-dashboard-readme.md](docs/kds-dashboard-readme.md).

## Docker compose

Launch the full stack with:

```bash
docker compose up --build
```

The reverse proxy exposes the Angular frontend at `http://localhost:8080/web` and the backend API at `http://localhost:8080/api`.
The backend publishes a health check at `/api/health` and interactive API documentation via Scalar at `/api/scalar` (in development mode).

### Database

The backend uses Entity Framework Core with a SQLite database. Migrations are located under `backend/Data/Migrations` and are applied automatically on startup.

### Tests

Run unit tests with:

```bash
dotnet test backend/Takeaway.Api.Tests/Takeaway.Api.Tests.csproj
```
