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
