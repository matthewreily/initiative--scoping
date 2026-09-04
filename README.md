# Initiative Scoping & Cost Tracking

ASP.NET Core 8 MVC application for scoping engineering initiatives, pricing them against BU/seniority/location/vendor rate cards, and tracking forecast vs. actuals (Planview at launch, Jira fast-follow).

## Solution layout

```
src/
  InitiativeScoping.Domain          entities, enums, pure domain rules (RateResolver, ForecastCalculator)
  InitiativeScoping.Application     use-case abstractions (IActualsSource, ICurrentUser), roles/policies
  InitiativeScoping.Infrastructure  EF Core AppDbContext, migrations (PostgreSQL), seeding, connectors
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

CI enforces **≥ 80 % line coverage** (EF migrations excluded). Reproduce locally with:

```bash
dotnet test --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory TestResults
python3 tests/coverage-check.py TestResults 80
```

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

### Actuals, roster and variance

- **People roster** (`/Admin/People`, Administrator) – each person carries the rate-card dimensions (resource type, BU, seniority, location, internal/vendor) plus semicolon-separated **external IDs** (Planview resource id, e-mail, Jira account id…). External IDs are unique across the roster (case-insensitive); inactive people never match new imports; people with imported actuals can only be deactivated, not deleted. **CSV import/export** (`Import`, `Export`, `Template` on the People page): columns `DisplayName,ExternalIds,ResourceType,BusinessUnit,Seniority,Location,ResourcingClass[,IsActive]`; rows are matched to existing people by any shared external ID, then by display name, and updated in place (others are added); the whole file is rejected on any parse, unknown-reference, duplicate or ambiguous-match error.
- **Source mappings** – on the initiative page an Owner/Administrator maps external project ids per source (`Csv`, `Planview`, `Jira`). `(Source, ExternalProjectId)` is unique case-insensitively.
- **Import** (`/Actuals`, Administrator or Finance/PMO) – upload a CSV `ExternalProjectId,ExternalPersonId,WorkDate,Hours[,Cost][,Reference]` (`WorkDate` = `yyyy-MM-dd`; template at `/Actuals/Template`). Files with any invalid row are rejected before anything is written. Rows are matched to an initiative via source mappings and to a person via external IDs; a row missing either lands in the **unmapped queue**. `Cost` from the source wins; otherwise cost = hours × the exact published rate in effect on the work date (no fallback — a missing rate leaves the row *unpriced*, counted as $0 and flagged). Rows whose `(Source, Reference)` was already imported are skipped, so re-uploading a file is idempotent. Every import records who/when/file/counts/log; `/Actuals/Imports/{id}` lists its rows.
- **Unmapped review** (`/Actuals/Unmapped`) – assign an initiative and/or person to a row (re-priced on assignment, audited) or *Re-apply mappings* after adding mappings/people.
- **Adjustments** – initiative managers add hours/cost adjustments with a mandatory reason (creator and time recorded, audited); they are included in variance.
- **Variance** (`/Initiatives/{id}/Actuals` and the summary on the initiative page) – mapped actuals + adjustments vs. the **current** baseline, in total and by phase (actuals bucketed by work date) and by resource type (from the person's roster record). Cost variance % is compared with the initiative's `VarianceThresholdPct` (default `Variance:DefaultThresholdPct`). Historical baselines are never touched by imports, rate changes or roster edits. **ETC/EAC**: the estimate to complete is schedule-based – the baseline cost of work not yet elapsed as of today (future phases in full, the in-progress phase pro-rated by remaining calendar days, finished phases zero) – and EAC = actual to date + ETC; the *projected variance* (EAC − baseline) is shown alongside spent-to-date variance and flagged against the same threshold.
- `IActualsSource` is the connector seam: the CSV upload and future Planview/Jira connectors feed the same `IActualsImporter`.

### Portfolio dashboard and exports

- **Portfolio** (`/Portfolio`, any signed-in role) – one row per initiative with live forecast (internal/vendor split), current baseline, actuals + adjustments, cost variance and %, burn bar, and badges for threshold breaches, unpriced forecast/actuals and open re-baseline requests; rollups by business unit and by status. Filter by status / business unit; Complete and Cancelled initiatives are hidden unless *Include Complete/Cancelled* is checked. All numbers come from `PortfolioCalculator`, which reuses `ForecastCalculator` and `VarianceCalculator`, so the dashboard always agrees with the initiative pages.
- **Exports** (Administrator or Finance/PMO) – `/Portfolio/Export?format=csv|xlsx` (respects the current filters) and `/Initiatives/{id}/Export?format=csv|xlsx` (summary, forecast lines, current baseline lines, variance by phase / resource type, actual entries, adjustments). XLSX uses one worksheet per table; CSV concatenates tables separated by a blank line and a `# <table>` marker. Unknown formats return 400. Every export writes an `Export` audit event.

### PostgreSQL

```bash
docker compose up -d postgres
export ASPNETCORE_ENVIRONMENT=Staging          # Production disables Auth:UseDevelopmentAuth
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=initiative_scoping;Username=postgres;Password=devpass"
export Database__MigrateOnStartup=true Database__SeedOnStartup=true Auth__UseDevelopmentAuth=true
dotnet run --project src/InitiativeScoping.Web
```

Or run the whole stack as containers: `docker compose up --build` (web on http://localhost:8080, dev auth, migrated + seeded).

Migrations live in `InitiativeScoping.Infrastructure` and target PostgreSQL (Npgsql). `DateTimeOffset` columns map to `timestamp with time zone`, so all timestamps must be UTC (the app uses `TimeProvider.GetUtcNow()` throughout):

```bash
dotnet ef migrations add <Name> -p src/InitiativeScoping.Infrastructure -s src/InitiativeScoping.Web -o Persistence/Migrations -- --Database:Provider=PostgreSql
```

To apply migrations without starting the web server (used by the Cloud Run migrate job): `dotnet InitiativeScoping.Web.dll --migrate`.

## Deployment (GCP)

See [`deploy/gcp/README.md`](deploy/gcp/README.md): Terraform for Cloud Run + Cloud SQL (PostgreSQL 16) + Secret Manager + Artifact Registry + Workload Identity Federation, and `.github/workflows/deploy.yml` which builds the image, runs the migrate job, deploys and smoke-tests `/health`.

## Authentication (non-development)

Microsoft Entra ID via OpenID Connect (`Microsoft.Identity.Web`). Configure `AzureAd:TenantId`/`ClientId` (+ `ClientSecret` via user secrets or environment) and assign app roles named `Administrator`, `InitiativeOwner`, `Contributor`, `Viewer`, `FinancePmo` in the app registration.

## Configuration keys

| Key | Purpose |
|-----|---------|
| `Database:Provider` | `PostgreSql` (default) or `Sqlite` |
| `Database:MigrateOnStartup` | Apply migrations (PostgreSQL) / create schema (SQLite) at startup |
| `ForwardedHeaders:Enabled` | Honour `X-Forwarded-For/Proto` from a TLS-terminating proxy (set in the container image; required for correct OIDC redirect URIs behind Cloud Run) |
| `Database:SeedOnStartup` | Seed a sample BU, resource types, sizing conversions, and a published rate card |
| `Auth:UseDevelopmentAuth` | Bypass Entra ID with a fixed dev identity (ignored in Production) |
| `Variance:DefaultThresholdPct` | Cost-variance % that flags an initiative when it has no threshold of its own (default 10) |
| `Limits:MaxRequestBodyBytes` | Kestrel/multipart request body cap (default 12 MB; actuals CSV uploads are capped at 10 MB regardless) |
| `Culture` | Request culture used for currency/date formatting (default `en-US`) |
| `OpenTelemetry:Enabled` | Register OpenTelemetry tracing/metrics (default `true`; instrumentation is in-process only until an exporter endpoint is set) |
| `OpenTelemetry:ServiceName` | `service.name` resource attribute (default `initiative-scoping`) |
| `OpenTelemetry:Otlp:Endpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP/gRPC collector endpoint, e.g. `http://localhost:4317`; when blank nothing is exported |
| `DataProtection:KmsKeyName` | Cloud KMS key (`projects/…/locations/…/keyRings/…/cryptoKeys/…`) used to wrap Data Protection keys at rest; when blank keys are stored unwrapped (local development) |

## Operations and hardening

- **Read paths** – the portfolio dashboard, exports and initiative pages load with `AsNoTracking` + `AsSplitQuery` (`Web/Services/PortfolioQueries.cs`) so wide graphs (phases × allocations × baseline lines) don't multiply into cartesian result sets. Migration `Phase7Indexes` adds indexes for the hot filters: `Initiatives(Status)`, `ForecastBaselines(InitiativeId, IsCurrent)`, `RebaselineRequests(InitiativeId, Status)`, `ActualEntries(InitiativeId, IsUnmapped, WorkDate)`, `ActualEntries(IsUnmapped)`, `RateCards(Status, EffectiveStart)`, `AuditEvents(At)`, `AuditEvents(Action)`.
- **HTTP hardening** – every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy` and a `Content-Security-Policy` that only allows same-origin assets (all JS/CSS is served from `wwwroot/lib`; the policy allows form posts to `login.microsoftonline.com` for Entra sign-in). HSTS and the generic error page are enabled outside Development. 403/404/413 responses re-execute to `/Home/Status` for a friendly page while preserving the status code.
- **Uploads** – actuals imports are capped at 10 MB by a controller check that rejects oversize files with a message and no DB writes; the endpoint's `[RequestSizeLimit]` transport cap sits at 12 MB so the friendly message is reachable rather than a connection reset.
- **Health** – `/health` (anonymous) runs an EF Core connectivity check; use it for load-balancer probes.
- **Data Protection keys** – the keys that encrypt auth/antiforgery/TempData cookies are persisted in the `DataProtectionKeys` table (`AddDataProtection().PersistKeysToDbContext`) so every instance and revision shares one key ring. When `DataProtection:KmsKeyName` is set, `Infrastructure/DataProtection/KmsXmlEncryptor.cs` wraps each key with Cloud KMS before it is stored (and unwraps on load); the runtime identity needs `roles/cloudkms.cryptoKeyEncrypterDecrypter` on that key. Keys written before KMS was enabled stay readable (unwrapped) until they expire; delete their rows to force a fresh wrapped key (signs everyone out once).
- **Observability (OpenTelemetry)** – `Web/Telemetry/TelemetryExtensions.cs` registers traces for incoming requests (ASP.NET Core, `/health` excluded), outgoing `HttpClient` calls and Npgsql database commands, plus metrics for ASP.NET Core, `HttpClient`, the .NET runtime and Npgsql. Application-level telemetry lives in `Application/AppTelemetry.cs` (source/meter `InitiativeScoping`): an `actuals.import` span with source/row counts, and counters `initiative_scoping.actuals.imports`, `initiative_scoping.actuals.records` (by outcome), `initiative_scoping.baselines.captured` (activation/rebaseline) and `initiative_scoping.initiatives.status_changes`. Serilog console lines carry `trace=`/`span=` so logs correlate with traces. Export is OTLP/gRPC and only happens when `OTEL_EXPORTER_OTLP_ENDPOINT` (or `OpenTelemetry:Otlp:Endpoint`) is set — locally, e.g. `docker run -p 4317:4317 -p 16686:16686 jaegertracing/all-in-one` then `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run`. On Cloud Run the Terraform stack runs the Google-Built OpenTelemetry Collector as a sidecar that forwards to Cloud Trace / Cloud Monitoring (see `deploy/gcp/README.md`).
- **Tests** – `dotnet test` runs domain unit tests and integration tests (in-process TestServer + throwaway SQLite DB per factory). `HardeningTests` cover headers, friendly error pages, upload limits and a 60-initiative portfolio load. See `HowTo.md` for day-to-day walkthroughs.
