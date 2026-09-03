# Microsoft Entra ID setup

The app authenticates users with Entra ID (OpenID Connect) and authorizes them with **app roles**
(`Administrator`, `InitiativeOwner`, `Contributor`, `Viewer`, `FinancePmo`). Each environment
(`dev`, `prod`) gets its own app registration so client IDs/secrets are never shared.

## 1. Get a tenant (once)

You need a Microsoft Entra tenant and an account in it with the **Application Developer** role
(or Global Administrator). Pick one:

| Situation | What to do |
|-----------|------------|
| Your company already uses Microsoft 365 / Azure | Ask an Entra admin to grant you *Application Developer* in that tenant (or to run the script below for you). |
| No tenant yet, want a free one for dev/test | Sign up for a free Azure account at <https://azure.microsoft.com/free> — it creates a tenant with you as Global Administrator. No subscription is needed for app registrations. |
| Want a throwaway dev tenant with test users | Join the Microsoft 365 Developer Program at <https://developer.microsoft.com/microsoft-365/dev-program> (eligibility applies) or create an extra tenant from the Entra admin center: <https://entra.microsoft.com> → Identity → Overview → **Manage tenants** → Create. |

After that, install the Azure CLI (<https://learn.microsoft.com/cli/azure/install-azure-cli>) and `jq`,
then sign in to the tenant:

```bash
az login --tenant <tenant-id-or-domain> --allow-no-subscriptions
```

`--allow-no-subscriptions` matters for a bare tenant without an Azure subscription.

## 2. Register the app

```bash
deploy/entra/register-app.sh dev            # before Terraform (no URL yet)
deploy/entra/register-app.sh prod
```

The script is idempotent and, per environment:

- creates (or reuses) the single-tenant app registration `Initiative Scoping (<env>)` with ID tokens enabled;
- defines the five app roles with fixed IDs (re-runs update in place);
- creates the enterprise application, sets **assignment required**, and assigns *you* the `Administrator` role;
- creates a 1-year client secret and prints it **once**;
- prints the `entra_tenant_id` / `entra_client_id` lines for `deploy/gcp/env/<env>.tfvars` and the `gcloud secrets versions add` command for the secret.

Copy the secret somewhere safe immediately (a password manager); it cannot be read back.

## 3. After `terraform apply`

Cloud Run assigns the URL, so the redirect URI is added afterwards:

```bash
deploy/entra/register-app.sh dev --add-url "$(terraform -chdir=deploy/gcp output -raw service_url)"
```

This adds `<url>/signin-oidc` as a redirect URI and `<url>/signout-callback-oidc` as the logout URL.

## 4. Grant access to other people

Entra admin center → **Enterprise applications** → `Initiative Scoping (<env>)` → **Users and groups** →
Add user/group → pick a role. Because assignment is required, unassigned users get an Entra error
before ever reaching the app. Use groups for anything beyond a handful of people.

## Rotating the secret

Re-run `deploy/entra/register-app.sh <env>`: it appends a new secret (old ones stay valid until you
delete them under *Certificates & secrets*), then store it with the printed `gcloud` command and
redeploy (Cloud Run reads the `latest` version at start-up).
