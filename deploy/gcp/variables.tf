variable "project_id" {
  type = string
}

variable "region" {
  type    = string
  default = "us-central1"
}

variable "app_name" {
  type    = string
  default = "initiative-scoping"
}

variable "environment" {
  type        = string
  description = "dev | prod (drives HA/backups/deletion protection)."
  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment must be dev or prod."
  }
}

variable "image_tag" {
  type        = string
  description = "Initial image tag; later tags are set by the deploy workflow."
  default     = "latest"
}

variable "db_tier" {
  type    = string
  default = "db-f1-micro" # use db-custom-1-3840 or larger for prod
}

variable "min_instances" {
  type    = number
  default = 0
}

variable "max_instances" {
  type    = number
  default = 3
}

variable "seed_reference_data" {
  type        = bool
  description = "Seed sample BUs/resource types/rate card on migrate (dev only)."
  default     = false
}

variable "entra_tenant_id" {
  type = string
}

variable "entra_client_id" {
  type = string
}

variable "github_repository" {
  type        = string
  description = "owner/repo allowed to deploy via Workload Identity Federation."
  default     = "matthewreily/initiative--scoping"
}

variable "enable_telemetry" {
  type        = bool
  description = "Run the Google-Built OpenTelemetry Collector as a Cloud Run sidecar and export traces/metrics to Cloud Trace / Cloud Monitoring."
  default     = true
}

variable "alert_emails" {
  type        = list(string)
  description = "Email addresses notified by the Cloud Monitoring alert policies. Empty disables alerting (policies without a channel are silent)."
  default     = []
}

variable "alert_5xx_per_minute" {
  type        = number
  description = "Sustained 5xx responses per minute that trigger the server-error alert."
  default     = 1
}

variable "alert_latency_p95_ms" {
  type        = number
  description = "p95 request latency (ms) that triggers the latency alert after 10 minutes."
  default     = 3000
}

variable "alert_sql_cpu_utilization" {
  type    = number
  default = 0.8
}

variable "alert_sql_disk_utilization" {
  type    = number
  default = 0.85
}

variable "otel_collector_image" {
  type        = string
  description = "Google-Built OpenTelemetry Collector image for the sidecar."
  default     = "us-docker.pkg.dev/cloud-ops-agents-artifacts/google-cloud-opentelemetry-collector/otelcol-google:0.159.0"
}
