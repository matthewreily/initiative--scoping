# Initiative Scoping & Cost Tracking

ASP.NET Core 8 MVC application for scoping engineering initiatives, pricing them against BU/seniority/location/vendor rate cards, and tracking forecast vs. actuals (Planview at launch, Jira fast-follow).

## Solution layout

```
src/
  InitiativeScoping.Domain          entities, enums, pure domain rules (RateResolver, ForecastCalculator)
  InitiativeScoping.Application     use-case abstractions (IActualsSource, ICurrentUser), roles/policies
  InitiativeScoping.Infrastructure  EF Core AppDbContext, migrations (SQL Server), seeding, connectors
  InitiativeScoping.Web             MVC controllers/views, API controllers, auth wiring
tests/
  InitiativeScoping.Domain.Tests        xUnit unit tests for domain rules
  InitiativeScoping.Integration.Tests   WebApplicationFactory tests (SQLite, dev auth)
```

## Local development

Prerequisites: .NET 8 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/InitiativeScoping.Web
```

The `Development` environment uses SQLite (`initiative-scoping.dev.db`, created and seeded on startup) and a development auth scheme that signs every request in as `Dev User` with all roles (`appsettings.Development.json` → `Auth:Dev`). No Entra ID setup is needed locally.

The SQLite schema is created with `EnsureCreated` and is **not** migrated; after pulling model changes delete `src/InitiativeScoping.Web/initiative-scoping.dev.db*` and restart to rebuild and reseed it.

### Administration

Users in the `Administrator` role get an **Admin** nav link (`/Admin/...`) for configuration (spec §5.1):

- **Business Units** and **Resource Types** – CRUD with active/inactive flag; deletion is blocked while referenced (deactivate instead).
- **Rate Cards** – Draft → Published → Retired lifecycle with an effective start date. Entries are keyed by resource type × business unit × seniority × location × internal/vendor. Entries can be added inline or bulk-imported via CSV (`ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate`; merge or replace; the file is rejected as a whole if any row is invalid). Export and a template download are available. Published cards cannot be deleted – retire them so historical baselines stay reproducible.
- **Sizing** – T-shirt / story-point → hours conversions and optional allocation templates (phase × resource type × seniority percentages that must total 100%).

Every admin create/update/delete/publish/retire/import writes an `AuditEvent` row (user, timestamp, JSON diff).

### SQL Server

```bash
docker compose up -d sqlserver
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Default="Server=localhost,1433;Database=InitiativeScoping;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True"
export Database__MigrateOnStartup=true Database__SeedOnStartup=true
dotnet run --project src/InitiativeScoping.Web
```

Migrations live in `InitiativeScoping.Infrastructure` and target SQL Server:

```bash
dotnet ef migrations add <Name> -p src/InitiativeScoping.Infrastructure -s src/InitiativeScoping.Web
```

## Authentication (non-development)

Microsoft Entra ID via OpenID Connect (`Microsoft.Identity.Web`). Configure `AzureAd:TenantId`/`ClientId` (+ `ClientSecret` via user secrets or environment) and assign app roles named `Administrator`, `InitiativeOwner`, `Contributor`, `Viewer`, `FinancePmo` in the app registration.

## Configuration keys

| Key | Purpose |
|-----|---------|
| `Database:Provider` | `SqlServer` (default) or `Sqlite` |
| `Database:MigrateOnStartup` | Apply migrations (SQL Server) / create schema (SQLite) at startup |
| `Database:SeedOnStartup` | Seed a sample BU, resource types, sizing conversions, and a published rate card |
| `Auth:UseDevelopmentAuth` | Bypass Entra ID with a fixed dev identity (ignored in Production) |
