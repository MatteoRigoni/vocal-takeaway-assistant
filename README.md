# vocal-takeaway-assistant

Reusable take-away ordering system with voice/text orders.

## Backend domain

The domain entities, relationships and seed data are documented in [docs/domain-model.md](docs/domain-model.md).

### Database

The backend uses Entity Framework Core with a SQLite database. Migrations are located under `backend/Data/Migrations` and are applied automatically on startup.

### Tests

Run unit tests with:

```bash
dotnet test backend/Takeaway.Api.Tests/Takeaway.Api.Tests.csproj
```
