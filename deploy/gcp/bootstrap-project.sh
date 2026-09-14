#!/usr/bin/env bash
# One-time: create the GCP project + Terraform state bucket for an environment, then run the
# Terraform bootstrap (registry -> first image -> full apply). Idempotent; safe to re-run.
#
# Usage:
#   gcloud auth login && gcloud auth application-default login
#   deploy/gcp/bootstrap-project.sh <dev|prod> <billing-account-id>
#
# Reads project_id/region from deploy/gcp/env/<env>.tfvars and the bucket from env/<env>.gcs.tfbackend.
# The first Terraform apply needs an image to exist: dev builds it from this checkout; prod never
# builds -- it copies the image dev is currently running (the promotion workflow takes over after).
# Requires: gcloud, terraform, docker.
set -euo pipefail

ENV="${1:?usage: bootstrap-project.sh <dev|prod> <billing-account-id>}"
BILLING="${2:?billing account id, e.g. 00EDAE-09A2AC-75569D}"
[[ "$ENV" == dev || "$ENV" == prod ]] || { echo "env must be dev or prod" >&2; exit 1; }

cd "$(dirname "$0")"
tfvar() { sed -n "s/^$1 *= *\"\([^\"]*\)\".*/\1/p" "env/$ENV.tfvars"; }
PROJECT_ID=$(tfvar project_id)
REGION=$(tfvar region)
BUCKET=$(sed -n 's/^bucket *= *"\([^"]*\)".*/\1/p' "env/$ENV.gcs.tfbackend")
CLIENT_ID=$(tfvar entra_client_id)

[[ "$PROJECT_ID" != REPLACE* && "$BUCKET" != REPLACE* ]] || { echo "Fill in env/$ENV.tfvars and env/$ENV.gcs.tfbackend first" >&2; exit 1; }
[[ "$CLIENT_ID" != REPLACE* ]] || { echo "entra_client_id in env/$ENV.tfvars is still a placeholder: run deploy/entra/register-app.sh $ENV first" >&2; exit 1; }

if ! gcloud projects describe "$PROJECT_ID" >/dev/null 2>&1; then
  gcloud projects create "$PROJECT_ID" --name "Initiative Scoping ($ENV)"
fi
gcloud billing projects link "$PROJECT_ID" --billing-account "$BILLING" >/dev/null
gcloud services enable cloudresourcemanager.googleapis.com serviceusage.googleapis.com storage.googleapis.com --project "$PROJECT_ID"

if ! gcloud storage buckets describe "gs://$BUCKET" >/dev/null 2>&1; then
  gcloud storage buckets create "gs://$BUCKET" --project "$PROJECT_ID" --location "$REGION" --uniform-bucket-level-access
  gcloud storage buckets update "gs://$BUCKET" --versioning
fi

terraform init -reconfigure -backend-config="env/$ENV.gcs.tfbackend"
terraform apply -var-file="env/$ENV.tfvars" -target=google_artifact_registry_repository.images -auto-approve
REPO=$(terraform output -raw image_repository)
gcloud auth configure-docker "${REGION}-docker.pkg.dev" --quiet
if [[ "$ENV" == dev ]]; then
  IMAGE_TAG=latest
  docker build -t "$REPO:$IMAGE_TAG" ../..
  docker push "$REPO:$IMAGE_TAG"
else
  DEV_PROJECT=$(sed -n 's/^project_id *= *"\([^"]*\)".*/\1/p' env/dev.tfvars)
  DEV_IMAGE=$(gcloud run services describe "initiative-scoping-dev" --project "$DEV_PROJECT" --region "$REGION" \
    --format 'value(spec.template.spec.containers[].image)' | tr ';' '\n' | grep '/initiative-scoping:' | head -1)
  [[ -n "$DEV_IMAGE" ]] || { echo "Could not find the app image currently deployed to dev" >&2; exit 1; }
  IMAGE_TAG=${DEV_IMAGE##*:}
  echo "Seeding prod with the image dev is running: $DEV_IMAGE"
  docker pull "$DEV_IMAGE"
  docker tag "$DEV_IMAGE" "$REPO:$IMAGE_TAG"
  docker push "$REPO:$IMAGE_TAG"
fi
terraform apply -var-file="env/$ENV.tfvars" -var "image_tag=$IMAGE_TAG"

cat <<EOF

==== $ENV provisioned ($PROJECT_ID) ====
Next:
  1. Entra client secret:  printf '%s' "\$CLIENT_SECRET" | gcloud secrets versions add $(terraform output -raw oidc_client_secret_id) --project $PROJECT_ID --data-file=-
                           gcloud run services update $(terraform output -raw cloud_run_service) --project $PROJECT_ID --region $REGION --update-labels oidc-secret-rolled=\$(date +%s)
  2. First migration:      gcloud run jobs execute $(terraform output -raw migrate_job) --project $PROJECT_ID --region $REGION --wait
  3. Redirect URI:         deploy/entra/register-app.sh $ENV --add-url $(terraform output -raw service_url)
  4. GitHub environment '$ENV' variables:
       GCP_PROJECT_ID=$PROJECT_ID
       GCP_REGION=$REGION
       GCP_WORKLOAD_IDENTITY_PROVIDER=$(terraform output -raw github_workload_identity_provider)
       GCP_DEPLOYER_SERVICE_ACCOUNT=$(terraform output -raw github_deployer_service_account)
EOF
if [[ "$ENV" == prod ]]; then
  cat <<EOF
  5. Re-apply dev so the prod deployer can pull dev images (image_pull_members in env/dev.tfvars):
       terraform init -reconfigure -backend-config=env/dev.gcs.tfbackend && terraform apply -var-file=env/dev.tfvars
  6. Repository variable DEV_IMAGE_REPOSITORY = dev's image_repository output; add required reviewers on the 'prod' environment.
EOF
fi
