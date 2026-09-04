#!/usr/bin/env bash
# Month-to-date GCP spend by service from the Cloud Billing BigQuery export.
#
# Usage: deploy/gcp/spend.sh [PROJECT_ID] [DATASET] [YYYY-MM]
#   PROJECT_ID  project that hosts the export dataset (default: initiative-scoping-dev)
#   DATASET     export dataset name (default: billing_export)
#   YYYY-MM     month to report (default: current month)
#
# Requires: gcloud auth (bq CLI ships with the SDK) and the billing export enabled at
# Billing -> Billing export -> Standard usage cost pointing at PROJECT_ID.DATASET.
set -euo pipefail

project="${1:-initiative-scoping-dev}"
dataset="${2:-billing_export}"
month="${3:-$(date -u +%Y-%m)}"

[[ "$project" =~ ^[a-z][a-z0-9-]{4,29}$ ]] || { echo "invalid project id: $project" >&2; exit 2; }
[[ "$dataset" =~ ^[A-Za-z0-9_]{1,1024}$ ]] || { echo "invalid dataset: $dataset" >&2; exit 2; }
[[ "$month" =~ ^[0-9]{4}-(0[1-9]|1[0-2])$ ]] || { echo "invalid month (YYYY-MM): $month" >&2; exit 2; }

table=$(bq --project_id "$project" ls --format=json "$dataset" \
  | python3 -c 'import json,sys; s=sys.stdin.read().strip(); t=[x["tableReference"]["tableId"] for x in (json.loads(s) if s else []) if x["tableReference"]["tableId"].startswith("gcp_billing_export_v1_")]; print(t[0] if t else "")')

if [[ -z "$table" ]]; then
  echo "No gcp_billing_export_v1_* table in $project.$dataset yet (export not enabled, or first load pending — allow ~24h)." >&2
  exit 1
fi

bq --project_id "$project" query --nouse_legacy_sql --format=pretty \
  --parameter="month:STRING:${month//-/}" --parameter="project:STRING:$project" "
WITH line_items AS (
  SELECT service.description AS svc,
         cost,
         IFNULL((SELECT SUM(c.amount) FROM UNNEST(credits) c), 0) AS credits,
         currency
  FROM \`$project.$dataset.$table\`
  WHERE invoice.month = @month
    AND project.id = @project
)
SELECT IFNULL(svc, 'TOTAL') AS service,
       ROUND(SUM(cost), 2)              AS cost,
       ROUND(SUM(credits), 2)           AS credits,
       ROUND(SUM(cost) + SUM(credits), 2) AS net,
       ANY_VALUE(currency)              AS currency
FROM line_items
GROUP BY ROLLUP(svc)
ORDER BY svc IS NULL, net DESC"
