# End-to-end tests (Playwright)

Browser tests that exercise the running application the way a user does. They complement the
xUnit suites under `tests/` (which run in-process) by catching layout, client-side validation,
JavaScript and full-request/redirect problems.

## How it works

- `playwright.config.ts` starts the web app itself (`dotnet run --project ../src/InitiativeScoping.Web`)
  in the `Development` environment on `http://127.0.0.1:5199`, waits for `/health`, then runs the specs
  in headless Chromium. The .NET SDK is the only thing it needs besides Node.
- **Auth**: the app's development auth scheme is on (`Auth:UseDevelopmentAuth=true`), so every request
  is signed in as `Dev User` with the `Admin` role. No Entra ID, secrets or browser login are involved.
- **Data**: a throw-away SQLite database at `e2e/.e2e-data/e2e.db` is deleted and re-created (with the
  standard seed data: business units, resource types, seniority levels, a published rate card, T-shirt
  sizes and templates) at the start of every run. Tests create their own uniquely named records and never
  depend on each other.
- Tests run serially (one worker) because they share one app instance and database.

## Running locally

Prerequisites: .NET 8 SDK, Node 20+.

```bash
cd e2e
npm ci                                       # install @playwright/test
npx playwright install --with-deps chromium  # download the browser (first time / after a Playwright upgrade)

npm test                 # headless run, list reporter
npm run test:headed      # watch the browser
npm run test:ui          # Playwright UI mode: pick tests, time-travel through steps
npm run report           # open the HTML report of the last run (screenshots, traces, videos of failures)

npx playwright test tests/lifecycle.spec.ts            # one file
npx playwright test -g "rate card"                     # tests whose title matches
npx playwright test --debug                            # step through with the inspector
```

Free port 5199 if something else is using it, or point the suite at an app you are already running
(no web server is started, the database is whatever that app uses):

```bash
dotnet run --project src/InitiativeScoping.Web     # in another terminal → http://localhost:5086
E2E_BASE_URL=http://localhost:5086 npm test
```

`npm run typecheck` type-checks the specs (CI runs this before the tests).

## What is covered

| Spec | Flows |
| --- | --- |
| `smoke.spec.ts` | `/health`, home + nav, every main page returns 200, `g i` / `g p` keyboard navigation |
| `initiative-planning.spec.ts` | New initiative → add phase → Apply relative size → priced forecast → "Why this number" drill-down; initiative shows in list, global search and Portfolio; Portfolio CSV export contains it |
| `lifecycle.spec.ts` | Activate is disabled on an empty draft, enabled once priced; activation captures baseline v1, locks scope and shows on the History tab; notes can be added |
| `admin-rate-cards.spec.ts` | Create rate card → CSV import (incl. a new resource type) → entries shown → CSV export round-trips |
| `ui-regressions.spec.ts` | Edit initiative: default % values pass client validation and the sizing row inputs share a baseline (PRs #64/#65); Allocations table scrolls sideways instead of wrapping/clipping (PR #63) |

Shared helpers (`createInitiative`, `addPhase`, `applySize`, `uniqueName`) live in `tests/helpers.ts`.

## Writing a new test

1. Add a `*.spec.ts` under `tests/`; import helpers from `./helpers`.
2. Prefer role/label based locators (`getByRole`, `getByLabel`) and the stable ids already in the views
   (`#allocations-table`, `#tab-costs`, `#global-search`, …) over CSS classes.
3. Give created records a unique name (`uniqueName('E2E …')`) so tests stay independent of each other and
   of leftovers when running against a shared instance.
4. Run it with `npx playwright test tests/<file>.spec.ts --headed` while developing.

## CI

The `e2e` job in `.github/workflows/ci.yml` builds the web project, installs Chromium, type-checks and runs
the suite on every pull request and push to `main`. On failure the HTML report, screenshots, traces and
videos are attached to the run as the `playwright-report` artifact — download it and run
`npx playwright show-report <extracted folder>/playwright-report` to inspect, or open a `trace.zip` at
https://trace.playwright.dev.
