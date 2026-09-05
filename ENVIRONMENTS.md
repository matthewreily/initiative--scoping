# Environments & links

Where everything for this app lives. Keep this file current when an environment is added or a resource is renamed;
values marked *Terraform output* can be re-derived with `terraform -chdir=deploy/gcp output` after `terraform init -backend-config=env/<env>.gcs.tfbackend`.

## Source & delivery

| What | Where |
|---|---|
| Repository | <https://github.com/matthewreily/initiative--scoping> |
| CI (build + test + 80% coverage gate) | <https://github.com/matthewreily/initiative--scoping/actions/workflows/ci.yml> |
| CD to dev (on merge to `main`) | <https://github.com/matthewreily/initiative--scoping/actions/workflows/deploy.yml> |
| Container images | `us-central1-docker.pkg.dev/initiative-scoping-dev/initiative-scoping/initiative-scoping` (Artifact Registry) |
| Docs | `README.md` (overview/run locally), `HowTo.md` (using the app), `deploy/gcp/README.md` (infra), `deploy/entra/README.md` (identity) |

## dev (GCP project `initiative-scoping-dev`, region `us-central1`)

| What | Where |
|---|---|
| Application | <https://initiative-scoping-dev-xte6sgnzaa-uc.a.run.app> (alias <https://initiative-scoping-dev-725421595059.us-central1.run.app>) |
| Health check (no login) | <https://initiative-scoping-dev-xte6sgnzaa-uc.a.run.app/health> |
| Cloud Run service `initiative-scoping-dev` | <https://console.cloud.google.com/run/detail/us-central1/initiative-scoping-dev/revisions?project=initiative-scoping-dev> |
| Cloud Run migration job `initiative-scoping-dev-migrate` | <https://console.cloud.google.com/run/jobs?project=initiative-scoping-dev> |
| Cloud SQL (PostgreSQL 16) `initiative-scoping-dev-pg`, database `initiative_scoping` | <https://console.cloud.google.com/sql/instances/initiative-scoping-dev-pg/overview?project=initiative-scoping-dev> |
| Secret Manager (DB connection string, Entra client secret, OTel collector config) | <https://console.cloud.google.com/security/secret-manager?project=initiative-scoping-dev> |
| Cloud KMS key for Data Protection keys | <https://console.cloud.google.com/security/kms?project=initiative-scoping-dev> |
| Logs | <https://console.cloud.google.com/logs/query?project=initiative-scoping-dev> |
| Traces (OpenTelemetry → Cloud Trace) | <https://console.cloud.google.com/traces/list?project=initiative-scoping-dev> |
| Monitoring alerts (7 policies, email to `alert_emails`) & uptime check | <https://console.cloud.google.com/monitoring/alerting?project=initiative-scoping-dev> |
| Billing reports | <https://console.cloud.google.com/billing/00EDAE-09A2AC-75569D/reports?project=initiative-scoping-dev> |
| Billing export (BigQuery dataset `billing_export`) | <https://console.cloud.google.com/bigquery?project=initiative-scoping-dev> — query with `deploy/gcp/spend.sh` |
| Terraform | `deploy/gcp`, vars `deploy/gcp/env/dev.tfvars`, state bucket in `deploy/gcp/env/dev.gcs.tfbackend` |
| Deploy identity | Workload Identity Federation → `initiative-scoping-dev-deploy` service account (no keys) |

### Identity (Microsoft Entra ID)

| What | Where |
|---|---|
| Tenant | `f0f37d2f-1252-4242-8058-8b307b86b0b5` |
| App registration `Initiative Scoping (dev)` (client `488767c9-e55d-441f-962c-816cbc1f40fc`) | <https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/488767c9-e55d-441f-962c-816cbc1f40fc> |
| Assign users/groups to app roles | Entra admin center → Enterprise applications → *Initiative Scoping (dev)* → Users and groups |
| Script | `deploy/entra/register-app.sh dev` |

## prod

Not provisioned yet. Follow `deploy/gcp/README.md` (one-time setup) with `deploy/gcp/env/prod.tfvars` and `deploy/entra/register-app.sh prod`, then add its rows here.

## Local development

| What | Where |
|---|---|
| App | <http://localhost:5086> (`dotnet run --project src/InitiativeScoping.Web`), dev auth bypass as Administrator |
| Database | SQLite `src/InitiativeScoping.Web/initiative-scoping.dev.db` (auto-created and seeded) |
| PostgreSQL alternative | `docker compose up` (see `docker-compose.yml`) |
