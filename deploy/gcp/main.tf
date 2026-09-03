terraform {
  required_version = ">= 1.6"
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
  # Configure remote state per environment, e.g. via `-backend-config=env/<env>.gcs.tfbackend`.
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

locals {
  name       = "${var.app_name}-${var.environment}"
  image_repo = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.images.repository_id}/${var.app_name}"
  sql_conn   = google_sql_database_instance.db.connection_name
  db_conn_string = join(";", [
    "Host=/cloudsql/${local.sql_conn}",
    "Database=${google_sql_database.app.name}",
    "Username=${google_sql_user.app.name}",
    "Password=${random_password.db.result}",
    "SSL Mode=Disable",
    "Maximum Pool Size=20",
  ])
}

resource "google_project_service" "apis" {
  for_each = toset([
    "run.googleapis.com",
    "sqladmin.googleapis.com",
    "secretmanager.googleapis.com",
    "artifactregistry.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "cloudscheduler.googleapis.com",
    "sts.googleapis.com",
  ])
  service            = each.value
  disable_on_destroy = false
}

# ---------- Images ----------
resource "google_artifact_registry_repository" "images" {
  location      = var.region
  repository_id = var.app_name
  format        = "DOCKER"
  depends_on    = [google_project_service.apis]
}

# ---------- Database ----------
resource "google_sql_database_instance" "db" {
  name                = "${local.name}-pg"
  database_version    = "POSTGRES_16"
  region              = var.region
  deletion_protection = var.environment == "prod"

  settings {
    tier              = var.db_tier
    availability_type = var.environment == "prod" ? "REGIONAL" : "ZONAL"
    disk_autoresize   = true
    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = var.environment == "prod"
    }
    ip_configuration {
      # Public IP with no authorized networks: reachable only via the Cloud SQL Auth Proxy
      # (IAM-authenticated, TLS) that Cloud Run mounts at /cloudsql. Avoids needing a VPC connector.
      ipv4_enabled = true
      ssl_mode     = "ENCRYPTED_ONLY"
    }
    insights_config {
      query_insights_enabled = true
    }
  }
  depends_on = [google_project_service.apis]
}

resource "google_sql_database" "app" {
  name     = "initiative_scoping"
  instance = google_sql_database_instance.db.name
}

resource "random_password" "db" {
  length  = 32
  special = false
}

resource "google_sql_user" "app" {
  name     = "app"
  instance = google_sql_database_instance.db.name
  password = random_password.db.result
}

# ---------- Secrets ----------
resource "google_secret_manager_secret" "conn" {
  secret_id = "${local.name}-db-connection"
  replication {
    auto {}
  }
  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "conn" {
  secret      = google_secret_manager_secret.conn.id
  secret_data = local.db_conn_string
}

# Entra ID client secret is created empty here and populated out-of-band (never in Terraform state).
resource "google_secret_manager_secret" "oidc_client_secret" {
  secret_id = "${local.name}-oidc-client-secret"
  replication {
    auto {}
  }
  depends_on = [google_project_service.apis]
}

# ---------- Runtime identity ----------
resource "google_service_account" "run" {
  account_id   = "${local.name}-run"
  display_name = "${local.name} Cloud Run runtime"
}

resource "google_project_iam_member" "run_sql" {
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${google_service_account.run.email}"
}

resource "google_secret_manager_secret_iam_member" "run_conn" {
  secret_id = google_secret_manager_secret.conn.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.run.email}"
}

resource "google_secret_manager_secret_iam_member" "run_oidc" {
  secret_id = google_secret_manager_secret.oidc_client_secret.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.run.email}"
}

# ---------- Cloud Run service ----------
resource "google_cloud_run_v2_service" "web" {
  name     = local.name
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = google_service_account.run.email
    scaling {
      min_instance_count = var.min_instances
      max_instance_count = var.max_instances
    }
    volumes {
      name = "cloudsql"
      cloud_sql_instance {
        instances = [local.sql_conn]
      }
    }
    containers {
      image = "${local.image_repo}:${var.image_tag}"
      ports {
        container_port = 8080
      }
      resources {
        limits   = { cpu = "1", memory = "512Mi" }
        cpu_idle = true
      }
      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "ForwardedHeaders__Enabled"
        value = "true"
      }
      env {
        name  = "Database__Provider"
        value = "PostgreSql"
      }
      env {
        name  = "Database__MigrateOnStartup"
        value = "false"
      }
      env {
        name  = "AzureAd__TenantId"
        value = var.entra_tenant_id
      }
      env {
        name  = "AzureAd__ClientId"
        value = var.entra_client_id
      }
      env {
        name = "ConnectionStrings__Default"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.conn.secret_id
            version = "latest"
          }
        }
      }
      env {
        name = "AzureAd__ClientSecret"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.oidc_client_secret.secret_id
            version = "latest"
          }
        }
      }
      startup_probe {
        http_get {
          path = "/health"
        }
        initial_delay_seconds = 5
        period_seconds        = 5
        failure_threshold     = 6
      }
      liveness_probe {
        http_get {
          path = "/health"
        }
        period_seconds = 30
      }
    }
  }

  lifecycle {
    # The deploy workflow pins the image tag; don't fight it from Terraform.
    ignore_changes = [template[0].containers[0].image]
  }

  depends_on = [
    google_secret_manager_secret_version.conn,
    google_secret_manager_secret_iam_member.run_conn,
    google_secret_manager_secret_iam_member.run_oidc,
  ]
}

resource "google_cloud_run_v2_service_iam_member" "public" {
  name     = google_cloud_run_v2_service.web.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers" # authentication is enforced in-app by Entra ID
}

# ---------- Migration job (same image, `--migrate` entrypoint arg) ----------
resource "google_cloud_run_v2_job" "migrate" {
  name     = "${local.name}-migrate"
  location = var.region

  template {
    template {
      service_account = google_service_account.run.email
      max_retries     = 0
      volumes {
        name = "cloudsql"
        cloud_sql_instance {
          instances = [local.sql_conn]
        }
      }
      containers {
        image = "${local.image_repo}:${var.image_tag}"
        args  = ["--migrate"]
        volume_mounts {
          name       = "cloudsql"
          mount_path = "/cloudsql"
        }
        env {
          name  = "Database__Provider"
          value = "PostgreSql"
        }
        env {
          name  = "Database__SeedOnStartup"
          value = var.seed_reference_data ? "true" : "false"
        }
        env {
          name = "ConnectionStrings__Default"
          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.conn.secret_id
              version = "latest"
            }
          }
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].template[0].containers[0].image]
  }

  depends_on = [google_secret_manager_secret_version.conn, google_secret_manager_secret_iam_member.run_conn]
}

# ---------- GitHub Actions deploy identity (Workload Identity Federation, no SA keys) ----------
resource "google_service_account" "deployer" {
  account_id   = "${local.name}-deployer"
  display_name = "${local.name} GitHub Actions deployer"
}

resource "google_project_iam_member" "deployer_roles" {
  for_each = toset([
    "roles/run.developer",
    "roles/artifactregistry.writer",
  ])
  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_service_account_iam_member" "deployer_acts_as_runtime" {
  service_account_id = google_service_account.run.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "${local.name}-github"
  depends_on                = [google_project_service.apis]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github"
  attribute_mapping = {
    "google.subject"             = "assertion.sub"
    "attribute.repository"       = "assertion.repository"
    "attribute.repository_owner" = "assertion.repository_owner"
  }
  attribute_condition = "assertion.repository == \"${var.github_repository}\""
  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

resource "google_service_account_iam_member" "deployer_wif" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"
}
