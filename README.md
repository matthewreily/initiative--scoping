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

### Initiatives (scoping)

`/Initiatives` (spec §5.2–5.3) is readable by every role; Administrators, Initiative Owners and Contributors can create initiatives. The creator becomes the initiative's **Owner** member; per-initiative editing is limited to Administrators and members with the Owner/Contributor role, and member management to Administrators/Owners.

- **Phases** – planned start/end with sequence; every date change is recorded in `PhaseDateHistory` (old/new dates, who, why).
- **Allocations** – phase × resource type × seniority × location × internal/vendor × quantity × estimated hours. Seniority lives on the allocation, not the resource type.
- **Sizing** – *Direct* hours, or *Apply size* (T-shirt / story points) which looks up the admin conversion + allocation template, creates any missing template phases and generates allocation lines from the template percentages (optionally replacing existing lines).
- **Forecast** – live cost uses the published rate card in effect at each phase's planned start with exact-match rate resolution; lines with no matching rate show as **Unpriced** and the initiative forecast is flagged **Incomplete**. Rollups by phase, resource type and internal vs vendor, plus a Gantt-style phase timeline, are on the details page.
- Scope (phases/allocations/sizing) is editable only while the initiative is **Draft** or during an approved re-baseline (below).

### Lifecycle, baselines and re-baselining

- **Activate** (Administrator or Owner member) requires at least one phase, at least one allocation and no unpriced lines. It freezes the live forecast as **Forecast Baseline v1** (`ForecastBaseline` + per-line `HourlyRate`/`Cost`), sets the status to **Active** and locks scope. Baseline values never change when rate cards are later republished.
- **Status transitions**: Draft → Active (via Activate) / Cancelled; Active → OnHold / Complete / Cancelled; OnHold → Active / Cancelled. Complete and Cancelled are terminal. Only Draft initiatives can be deleted.
- **Re-baseline** (spec §5.4): an Owner requests a re-baseline with a reason → an **Administrator** approves (or rejects) from the initiative page or the `/Rebaselines` queue → scope is unlocked → the Owner **finalizes**, which captures baseline v*N+1*, marks it current and locks scope again. Every prior version is retained; `/Initiatives/{id}/Baselines` lists versions, line-level deltas vs. the previous version and the live-forecast drift vs. the current baseline. Withdrawing an open request re-locks scope; Cancelling an initiative withdraws any open request.
- **Audit log** – `/Audit` (all roles, filterable by entity/id/action/user) shows every configuration, scope, status, baseline and re-baseline event. Each initiative page links to its own history.

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
