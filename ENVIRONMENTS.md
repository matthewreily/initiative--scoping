# Environments & links

Where everything for this app lives. Keep this file current when an environment is added or a resource is renamed;
values marked *Terraform output* can be re-derived with `terraform -chdir=deploy/gcp output` after `terraform init -backend-config=env/<env>.gcs.tfbackend`.

## Source & delivery

| What | Where |
|---|---|
| Repository | <https://github.com/matthewreily/initiative--scoping> |
| CI (build + test + 80% coverage gate) | <https://github.com/matthewreily/initiative--scoping/actions/workflows/ci.yml> |
| CD: dev on merge to `main`, prod via `v*` tag / Run workflow (approval-gated) | <https://github.com/matthewreily/initiative--scoping/actions/workflows/deploy.yml> |
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
| Grant access (approve requests, roles) | In-app: Admin → Users. Entra app roles (Enterprise applications → *Initiative Scoping (dev)* → Users and groups) remain an optional override |
| Script | `deploy/entra/register-app.sh dev` |

## prod (GCP project `initiative-scoping-prod`, region `us-central1`)

| What | Where |
|---|---|
| Application | <https://initiative-scoping-prod-nthhtuct6q-uc.a.run.app> (Entra redirect registered for this URL; alias <https://initiative-scoping-prod-561297293406.us-central1.run.app> needs `register-app.sh prod --add-url`) |
| Health check (no login) | <https://initiative-scoping-prod-nthhtuct6q-uc.a.run.app/health> |
| Cloud Run service `initiative-scoping-prod` | <https://console.cloud.google.com/run/detail/us-central1/initiative-scoping-prod/revisions?project=initiative-scoping-prod> |
| Cloud Run migration job `initiative-scoping-prod-migrate` | <https://console.cloud.google.com/run/jobs?project=initiative-scoping-prod> |
| Cloud SQL (PostgreSQL 16, regional HA, backups + PITR, deletion protection) `initiative-scoping-prod-pg` | <https://console.cloud.google.com/sql/instances/initiative-scoping-prod-pg/overview?project=initiative-scoping-prod> |
| Secret Manager (DB connection string, Entra client secret, OTel collector config) | <https://console.cloud.google.com/security/secret-manager?project=initiative-scoping-prod> |
| Cloud KMS key for Data Protection keys | <https://console.cloud.google.com/security/kms?project=initiative-scoping-prod> |
| Logs | <https://console.cloud.google.com/logs/query?project=initiative-scoping-prod> |
| Traces (OpenTelemetry → Cloud Trace) | <https://console.cloud.google.com/traces/list?project=initiative-scoping-prod> |
| Monitoring alerts & uptime check | <https://console.cloud.google.com/monitoring/alerting?project=initiative-scoping-prod> |
| Billing reports | <https://console.cloud.google.com/billing/00EDAE-09A2AC-75569D/reports?project=initiative-scoping-prod> |
| Container images (copied from dev by digest, tagged with the commit SHA) | `us-central1-docker.pkg.dev/initiative-scoping-prod/initiative-scoping/initiative-scoping` |
| Terraform | `deploy/gcp`, vars `deploy/gcp/env/prod.tfvars`, state bucket `initiative-scoping-prod-tfstate` (`deploy/gcp/env/prod.gcs.tfbackend`) |
| Deploy identity | Workload Identity Federation → `initiative-scoping-prod-deploy@initiative-scoping-prod.iam.gserviceaccount.com` (no keys; also has read on the dev image registry) |
| Release | push a `v*` tag on a commit that has deployed to dev (or *Run workflow* → `sha`), then approve the `prod` deployment in Actions (GitHub environment `prod`, required reviewer). First release: `v1.0.0`. See `deploy/gcp/README.md` → "Continuous deployment" |

### Identity (Microsoft Entra ID)

| What | Where |
|---|---|
| Tenant | `f0f37d2f-1252-4242-8058-8b307b86b0b5` |
| App registration `Initiative Scoping (prod)` (client `e1ab5424-6eed-4b2b-adfd-ba1cad05e9fc`) | <https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/e1ab5424-6eed-4b2b-adfd-ba1cad05e9fc> |
| Grant access (approve requests, roles) | In-app: Admin → Users (bootstrap admin: `me@mattreily.com`) |
| Script | `deploy/entra/register-app.sh prod` |
