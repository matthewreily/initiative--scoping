output "service_url" {
  value = google_cloud_run_v2_service.web.uri
}

output "image_repository" {
  value = local.image_repo
}

output "cloud_run_service" {
  value = google_cloud_run_v2_service.web.name
}

output "migrate_job" {
  value = google_cloud_run_v2_job.migrate.name
}

output "sql_connection_name" {
  value = google_sql_database_instance.db.connection_name
}

output "oidc_client_secret_id" {
  description = "Populate with: gcloud secrets versions add <id> --data-file=-"
  value       = google_secret_manager_secret.oidc_client_secret.secret_id
}

# Paste these into GitHub environment secrets/variables for the deploy workflow.
output "github_workload_identity_provider" {
  value = google_iam_workload_identity_pool_provider.github.name
}

output "github_deployer_service_account" {
  value = google_service_account.deployer.email
}
