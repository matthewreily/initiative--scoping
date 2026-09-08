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
2. **Disciplines** – `Admin → Disciplines` (e.g. *Engineering*, *QA*, *Product*, *Design*). Every resource type must belong to exactly one discipline, so create these first. Deactivate a discipline to hide it from new resource types; it can only be deleted once no resource type references it.
3. **Resource Types** – `Admin → Resource Types` (e.g. *Software Engineer*, *QA Analyst*, *Product Manager*), each assigned to a discipline from the list above.
4. **Vendors** – `Admin → Vendors`. One entry per external supplier (e.g. *Acme Consulting*). Vendors are a global catalog, not business units: rate-card rows, allocations and people reference a vendor whenever their class is *Vendor*. Deactivate a vendor to hide it from new selections; it can only be deleted once nothing references it.
5. **Rate Card** – `Admin → Rate Cards → New`. Give it a name and effective start date, then add entries: one row per *Resource type × Seniority × Location × Internal/Vendor (× Vendor)*. Rates are global – one card prices every business unit and initiative. For *Vendor* rows pick a specific vendor, or leave it blank for a generic vendor rate that applies to any vendor without its own row (a vendor-specific row always wins). Use *Import CSV* for bulk entry (download the template from the card page); the optional `Vendor` column holds the vendor name and legacy files with a `BusinessUnit` column still import (the column is ignored). **Publish** when complete; rates are only used from published cards. To change rates later, create a new card with a later effective date and retire the old one – history is preserved and existing baselines are unaffected.
6. **Sizing conversions** – `Admin → Sizing` maps T-shirt sizes / story points to total hours; **allocation templates** define how those hours split across phases and resource types.
7. **Work calendar** (needed for fixed-duration initiatives) – `Admin → Work calendar`. Set *hours per working day* (default 8) and add company holidays (weekdays only; Saturdays/Sundays are never working days). Working days = Mon–Fri minus holidays.
8. **Cost catalog** (optional) – `Admin → Cost catalog`. Standard non-labor items (software licenses first; also hardware, cloud, travel, other) with vendor, billing model (*One-time*, *Monthly*, *Annual*) and unit cost, so initiatives price them consistently. Deactivate an item to hide it from new lines; it can only be deleted once no initiative references it.
9. **People** (needed for actuals) – `Admin → People`. Each person carries an external id (Planview/Jira resource id), resource type, BU, seniority, location, class and – for *Vendor* class – the vendor, so imported hours can be priced (the People CSV has a matching optional `Vendor` column).

## 2. Scope an initiative (Owner / Contributor)

1. `Initiatives → New`: name, **sponsor business unit** plus any other **participating business units** (a checklist; the sponsor is always included and cannot be removed), planning mode, target start (and target end for fixed duration), sizing method (`Direct` hours or a relative size). The size dropdown only offers sizes that have an allocation template for the chosen method; add templates under `Admin → Sizing` first.
   - **Effort-driven** (default): you type hours per allocation; phase dates are informational.
   - **Fixed duration**: the schedule is the constraint. Phases must start on the target start, run back-to-back with no gaps or overlaps, and cover the window up to the target end before the initiative can be activated. Allocation hours are *computed*, never typed: `staffing % × working days in the phase × hours/day` per person (quantity multiplies the total in the forecast). Changing a phase's dates or the target window recomputes the hours; work-calendar changes (hours/day, holidays) apply the next time an allocation or phase is saved, not retroactively to draft initiatives.
2. Add **phases** with planned start/end dates (`Add phase`). Dates can be edited later; every change is kept in the phase's date history.
3. Add **allocations**: phase, **business unit** (limited to the initiative's participating BUs; defaults to the sponsor), resource type, seniority, location, class (Internal FTE / Vendor), **vendor** (required for *Vendor* class, hidden for internal), quantity and hours. The BU records which unit supplies the resource (for rollups); the rate comes from the global rate card and depends on resource type, seniority, location, class and vendor only, so one initiative can mix internal staff from several BUs with people from several vendors at different rates. The form only offers resource type / seniority / location / class / vendor combinations that have a published rate on the selected phase's start date, with a live hourly-rate preview; the server rejects any other combination. To make a new resource selectable, add it under `Admin → Resource types`, then price it in a **published** rate card. If no published card is effective on the phase start date yet, every combination is offered but the allocation stays unpriced (and blocks activation) until rates exist. Hours are typed (effort-driven) or staffing % (fixed duration; defaults to 100 %, i.e. every working hour in the phase, and the form previews the resulting hours as you change the phase or %). For relative sizing choose a size and *Apply size* to generate allocations from the template, then adjust. In fixed-duration mode *Apply size* splits the target window across the template's phases in proportion to their share of hours and derives the staffing % that yields the size's hours – e.g. “L in 12 weeks needs 1 FTE”. To switch an existing effort-driven initiative to fixed duration, set a target end; existing hours are converted to an equivalent staffing % per phase.
4. Watch the **Forecast** panel. Each line is priced from the published rate card in effect on the phase start date. A line marked **Unpriced** has no exact rate-card match – add the missing rate (or change the allocation) before activation.
5. Add **non-labor costs** (`Add non-labor cost`) for software licenses and other non-labor spend. Pick a catalog item (category, billing and unit cost prefill) or enter an ad-hoc line; set quantity (e.g. seats), scope it to one phase or the whole initiative, and optionally override the billing window with explicit dates. Billing is by whole periods counted from the start date – any partial month/year counts as a full one (15 Jan–14 Feb = 1 month, 15 Jan–15 Feb = 2; one-time bills once) – and the form previews the cost live. Lines recompute when phase or initiative dates change; a line whose window is empty (end before start, or its phase was deleted) blocks activation. The Forecast panel shows **Labor / Non-labor / Total**.
6. Add members (`Members`) so Owners/Contributors can edit.

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
6. Add **adjustments** (category, hours/cost with a reason) on the initiative's Actuals page for invoices, accruals or corrections. Use a non-labor category (e.g. *Software license*) for vendor invoices so they land against the non-labor baseline in the variance view.

## 5. Track variance

- Initiative → **Variance**: current baseline vs. mapped actuals + adjustments, by phase, resource type and cost category (labor vs. software license etc.), with the threshold flag (initiative-level `VarianceThresholdPct` or the global default).
- **Portfolio** (`/Portfolio`): one row per initiative with live forecast (internal/vendor/non-labor split), baseline, actuals, variance %, burn bar and badges (*Over threshold*, *Unpriced forecast/actuals*, *Re-baseline pending*), plus rollups by **sponsor BU** and status, and labor splits by **resourcing BU** (where the allocated people come from) and by **vendor**. Filter by status or BU; tick *Include Complete/Cancelled* to see closed work.
- **Export** (Administrator / FinancePmo): *Export CSV/XLSX* on the portfolio (honours current filters) or on an initiative (summary incl. participating BUs, forecast and baseline lines with BU and vendor, variance, actuals, adjustments). Exports are audited.

## 6. Audit

`Audit` lists every create/update/delete/publish/activate/import/export event with actor, timestamp and entity link. Filter by entity type and id.

## 7. Working with tables

- **Sort**: click a column header (or focus it and press Enter/Space) to sort ascending; click again for descending. Numbers, currency, percentages and dates sort numerically/chronologically; text sorts case-insensitively. Sorting is presentation-level – it re-orders the rows currently on the page only, so on filtered pages (e.g. Audit, Actuals) apply the filter first. Portfolio is sortable but read-only.
- **Select rows**: tables with a checkbox column (rate-card entries, People, Business units, Resource types, Disciplines, Vendors, Cost catalog, Holidays, Actuals entries, an initiative's Allocations and Non-labor costs) have a header *select all* that applies to the rows currently shown; the action bar shows *N selected* and its buttons are disabled until something is selected. Destructive actions ask for one confirmation.
- **Bulk actions**: *Save selected* / *Adjust by %* / *Delete selected* on rate-card entries (draft and published cards only; retired cards are read-only; rates must be ≥ 0); *Activate* / *Deactivate* / *Delete selected* on admin catalogs; *Assign selected* to an initiative and/or person on Actuals entries; *Delete selected* on allocations and non-labor costs (draft/unlocked initiatives only). Each changed or deleted row is audited individually.
- **Skipped rows**: guarded rows are never silently deleted – a catalog row that is still referenced (e.g. a BU used by initiatives, a person with imported actuals) is skipped and the result message says how many were deleted and how many were skipped and why. Deactivate instead to hide them from new selections. A single invalid value in a bulk save (e.g. a negative rate) rejects the whole save so nothing is half-applied.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Forecast line shows **Unpriced** | No published rate-card entry exactly matches resource type + BU + seniority + location + class on the phase start date. Add the entry; there is no fallback pricing by design. |
| New resource type / rate-card row not selectable on *Add allocation* | The rate card must be **Published** with an effective start on or before the phase start, the entry's BU must be the *initiative's* BU, and the resource type must be active. The allocation form narrows each dropdown as you pick the previous one – change phase / resource type first. |
| **Activate** is disabled | Check the guard list on the page: phases, allocations, dates, unpriced lines. |
| Import rejected | The error lists the first 10 offending lines. Fix the file and re-upload; nothing was written. |
| Rows imported as **unmapped** | Add a source mapping for the project id and/or a person with that external id, then assign in *Unmapped* or re-import. |
| Local dev DB errors after pulling | Development uses SQLite with `EnsureCreated`; delete `src/InitiativeScoping.Web/initiative-scoping.dev.db*` and restart. |
| 403 *Access denied* | Your Entra app role (or `Auth:Dev:Roles` locally) lacks the permission – see the roles table above. |
