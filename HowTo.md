# How to use Initiative Scoping & Cost Tracking

Task-oriented walkthroughs. Architecture, configuration keys and operational notes are in [README.md](README.md).

## Roles at a glance

| Task | Administrator | InitiativeOwner | Contributor | FinancePmo | Viewer |
|------|:-:|:-:|:-:|:-:|:-:|
| View initiatives, portfolio, variance | x | x | x | x | x |
| Create initiatives / edit scope (phases, allocations) | x | x* | x* | | |
| Activate, change status, request re-baseline | x | x* | | | |
| Approve / reject re-baseline | x | | | | |
| Manage BUs, resource types, rate cards, sizing, people | x | | | | |
| Import actuals, review unmapped, add adjustments | x | | | x | |
| Export CSV / XLSX | x | | | x | |

`*` also requires being a member of the initiative with the matching membership role (Owner or Contributor).

## 1. First-time setup (Administrator)

1. **Business Units** – `Admin → Business Units`. Create one per cost centre/BU. Deactivate rather than delete once initiatives reference a BU.
2. **Resource Types** – `Admin → Resource Types` (e.g. *Software Engineer*, *QA*, *Product Manager*), optionally grouped by discipline.
3. **Rate Card** – `Admin → Rate Cards → New`. Give it a name and effective start date, then add entries: one row per *Resource type × Business unit × Seniority × Location × Internal/Vendor*. Use *Import CSV* for bulk entry (download the template from the card page). **Publish** when complete; rates are only used from published cards. To change rates later, create a new card with a later effective date and retire the old one – history is preserved and existing baselines are unaffected.
4. **Sizing conversions** – `Admin → Sizing` maps T-shirt sizes / story points to total hours; **allocation templates** define how those hours split across phases and resource types.
5. **People** (needed for actuals) – `Admin → People`. Each person carries an external id (Planview/Jira resource id), resource type, BU, seniority, location and class so imported hours can be priced.

## 2. Scope an initiative (Owner / Contributor)

1. `Initiatives → New`: name, business unit, target start, sizing method (`Direct` hours or a relative size).
2. Add **phases** with planned start/end dates (`Add phase`). Dates can be edited later; every change is kept in the phase's date history.
3. Add **allocations**: phase, resource type, seniority, location, class (Internal FTE / Vendor), quantity and hours. For relative sizing choose a size and *Apply size* to generate allocations from the template, then adjust.
4. Watch the **Forecast** panel. Each line is priced from the published rate card in effect on the phase start date. A line marked **Unpriced** has no exact rate-card match – add the missing rate (or change the allocation) before activation.
5. Add members (`Members`) so Owners/Contributors can edit.

## 3. Activate and baseline (Owner)

1. When the forecast is complete, click **Activate**. Guards: at least one phase and one allocation, valid dates, no unpriced lines.
2. Activation snapshots the forecast as **Baseline v1** and locks scope (phases/allocations become read-only).
3. Status transitions: Active → On hold → Active, Active → Complete / Cancelled. Complete/Cancelled initiatives drop off the default portfolio view.

### Re-baseline

1. Owner: **Request re-baseline** with a reason.
2. Administrator: **Re-baselines** (nav bar) → **Approve** (scope unlocks) or **Reject**.
3. Owner edits phases/allocations, then **Finalize** to snapshot **v2**. All versions remain in `Baselines` with deltas; variance is always measured against the *current* version.

## 4. Load actuals (FinancePmo / Administrator)

1. Map each initiative to its external project: initiative page → **Source mappings** → source (`planview`, `jira`, `csv`) + external project id.
2. Prepare a CSV:

   ```text
   ExternalProjectId,ExternalPersonId,WorkDate,Hours[,Cost][,Reference]
   PV-1001,E12345,2026-03-04,7.5,,
   ```

   - `WorkDate` is `yyyy-MM-dd`; `Hours` > 0.
   - `Cost` is optional. Blank → priced from the person's roster attributes and the rate card in effect on `WorkDate`; supplied → used as-is.
   - `Reference` is optional; blank defaults to `<file>#<line>`. `(Source, Reference)` is the idempotency key – re-importing the same file skips already-loaded rows.
3. `Actuals → Import`. Files with any invalid row are rejected as a whole (nothing is written). Files over 10 MB are refused.
4. Review the import summary: imported / skipped / **unmapped** counts. Unmapped rows (unknown project or person) sit in `Actuals → Unmapped`; assign them to an initiative/person there, or fix the mapping/roster and re-import.
5. Rows flagged **Unpriced actuals** have no rate for the person's attributes on that date – add the rate to a published card.
6. Add **adjustments** (hours/cost with a reason) on the initiative's Actuals page for invoices, accruals or corrections.

## 5. Track variance

- Initiative → **Variance**: current baseline vs. mapped actuals + adjustments, by phase and resource type, with the threshold flag (initiative-level `VarianceThresholdPct` or the global default).
- **Portfolio** (`/Portfolio`): one row per initiative with live forecast (internal/vendor split), baseline, actuals, variance %, burn bar and badges (*Over threshold*, *Unpriced forecast/actuals*, *Re-baseline pending*), plus rollups by BU and status. Filter by status or BU; tick *Include Complete/Cancelled* to see closed work.
- **Export** (Administrator / FinancePmo): *Export CSV/XLSX* on the portfolio (honours current filters) or on an initiative (summary, forecast, baseline, variance, actuals, adjustments). Exports are audited.

## 6. Audit

`Audit` lists every create/update/delete/publish/activate/import/export event with actor, timestamp and entity link. Filter by entity type and id.

## 7. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Forecast line shows **Unpriced** | No published rate-card entry exactly matches resource type + BU + seniority + location + class on the phase start date. Add the entry; there is no fallback pricing by design. |
| **Activate** is disabled | Check the guard list on the page: phases, allocations, dates, unpriced lines. |
| Import rejected | The error lists the first 10 offending lines. Fix the file and re-upload; nothing was written. |
| Rows imported as **unmapped** | Add a source mapping for the project id and/or a person with that external id, then assign in *Unmapped* or re-import. |
| Local dev DB errors after pulling | Development uses SQLite with `EnsureCreated`; delete `src/InitiativeScoping.Web/initiative-scoping.dev.db*` and restart. |
| 403 *Access denied* | Your Entra app role (or `Auth:Dev:Roles` locally) lacks the permission – see the roles table above. |
