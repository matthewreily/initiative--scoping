#!/usr/bin/env bash
# Registers the Initiative Scoping app in Microsoft Entra ID and prints the values
# needed by deploy/gcp/env/<env>.tfvars and Secret Manager.
#
# Usage:
#   az login --tenant <tenant-id-or-domain> --allow-no-subscriptions
#   deploy/entra/register-app.sh <env> [app-base-url]
#     env           dev | prod (used in the display name)
#     app-base-url  https://<cloud-run-url>  (optional; add later with --add-url)
#   deploy/entra/register-app.sh <env> --add-url https://<cloud-run-url>
#
# Requires: Azure CLI (az) >= 2.60, jq. The signed-in user needs the
# "Application Developer" (or higher) Entra role.
set -euo pipefail

ENV="${1:?usage: register-app.sh <dev|prod> [app-base-url | --add-url url]}"
shift
APP_NAME="Initiative Scoping (${ENV})"
ROLES=(Administrator InitiativeOwner Contributor Viewer FinancePmo)

command -v az >/dev/null || { echo "az CLI not found: https://learn.microsoft.com/cli/azure/install-azure-cli" >&2; exit 1; }
command -v jq >/dev/null || { echo "jq not found" >&2; exit 1; }

TENANT_ID=$(az account show --query tenantId -o tsv)

redirect_uris() { # $1 = base url
  echo "$1/signin-oidc"
}
logout_uri() { echo "$1/signout-callback-oidc"; }

# --- --add-url: append a redirect URI to an existing registration and exit ---
if [[ "${1:-}" == "--add-url" ]]; then
  BASE="${2:?missing url}"
  APP_ID=$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)
  [[ -n "$APP_ID" ]] || { echo "App '$APP_NAME' not found" >&2; exit 1; }
  EXISTING=$(az ad app show --id "$APP_ID" --query 'web.redirectUris' -o json)
  NEW=$(echo "$EXISTING" | jq -c --arg u "$(redirect_uris "$BASE")" '. + [$u] | unique')
  az ad app update --id "$APP_ID" \
    --set "web.redirectUris=$NEW" \
    --set "web.logoutUrl=$(logout_uri "$BASE")" >/dev/null
  echo "Added $(redirect_uris "$BASE") to '$APP_NAME'."
  exit 0
fi

BASE_URL="${1:-}"

# --- app roles manifest ---
# Fixed ids so re-running the script updates roles in place instead of duplicating them.
ROLES_JSON=$(i=0; for r in "${ROLES[@]}"; do
  i=$((i + 1))
  jq -n --arg r "$r" --arg id "$(printf '5a1e0c00-0000-4000-8000-%012d' "$i")" '{
    allowedMemberTypes: ["User"],
    description: ($r + " role for Initiative Scoping"),
    displayName: $r,
    id: $id,
    isEnabled: true,
    value: $r
  }'
done | jq -s '.')

# --- create or reuse the registration ---
APP_ID=$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)
if [[ -z "$APP_ID" ]]; then
  ARGS=(--display-name "$APP_NAME" --sign-in-audience AzureADMyOrg --enable-id-token-issuance true)
  [[ -n "$BASE_URL" ]] && ARGS+=(--web-redirect-uris "$(redirect_uris "$BASE_URL")")
  APP_ID=$(az ad app create "${ARGS[@]}" --query appId -o tsv)
  echo "Created app registration $APP_ID"
else
  echo "Reusing existing app registration $APP_ID"
fi

az ad app update --id "$APP_ID" --app-roles "$ROLES_JSON" >/dev/null
[[ -n "$BASE_URL" ]] && az ad app update --id "$APP_ID" --set "web.logoutUrl=$(logout_uri "$BASE_URL")" >/dev/null

# Service principal (Enterprise application) so users/groups can be assigned to roles;
# require assignment so only assigned users can sign in.
SP_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query '[0].id' -o tsv)
if [[ -z "$SP_ID" ]]; then
  SP_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)
fi
az ad sp update --id "$SP_ID" --set appRoleAssignmentRequired=true >/dev/null

# Assign the signed-in user as Administrator so someone can get in on day one.
ME=$(az ad signed-in-user show --query id -o tsv)
ADMIN_ROLE_ID=$(echo "$ROLES_JSON" | jq -r '.[] | select(.value=="Administrator") | .id')
ALREADY=$(az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/$SP_ID/appRoleAssignedTo" \
  --query "value[?principalId=='$ME' && appRoleId=='$ADMIN_ROLE_ID'] | length(@)" -o tsv)
if [[ "$ALREADY" == "0" ]]; then
  az rest --method POST \
    --url "https://graph.microsoft.com/v1.0/servicePrincipals/$SP_ID/appRoleAssignedTo" \
    --body "$(jq -n --arg p "$ME" --arg r "$SP_ID" --arg a "$ADMIN_ROLE_ID" \
      '{principalId:$p, resourceId:$r, appRoleId:$a}')" >/dev/null
  echo "Assigned you the Administrator role."
fi

# Client secret (value is shown once; 12 months).
SECRET=$(az ad app credential reset --id "$APP_ID" --display-name "gcp-${ENV}" --years 1 \
  --append --query password -o tsv)

cat <<EOF

==== Entra values for deploy/gcp/env/${ENV}.tfvars ====
entra_tenant_id = "${TENANT_ID}"
entra_client_id = "${APP_ID}"

==== Client secret (store in Secret Manager; not shown again) ====
printf '%s' '${SECRET}' | gcloud secrets versions add \$(terraform output -raw oidc_client_secret_id) --data-file=-

Assign more users/groups to roles:
  Entra admin center -> Enterprise applications -> "${APP_NAME}" -> Users and groups
EOF
if [[ -z "$BASE_URL" ]]; then
  echo
  echo "No redirect URI set yet. After 'terraform apply' run:"
  echo "  deploy/entra/register-app.sh ${ENV} --add-url \$(terraform output -raw service_url)"
fi
