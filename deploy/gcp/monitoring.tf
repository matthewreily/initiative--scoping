# ---------- Alerting (Cloud Monitoring) ----------
# Everything here is created only when `alert_emails` is non-empty: an alert policy without a
# notification channel is silent, so there is no point provisioning one.

locals {
  alerting       = length(var.alert_emails) > 0
  alert_channels = [for c in google_monitoring_notification_channel.email : c.id]
  run_service_filter = join(" AND ", [
    "resource.type = \"cloud_run_revision\"",
    "resource.label.\"service_name\" = \"${google_cloud_run_v2_service.web.name}\"",
  ])
  sql_instance_filter = join(" AND ", [
    "resource.type = \"cloudsql_database\"",
    "resource.label.\"database_id\" = \"${var.project_id}:${google_sql_database_instance.db.name}\"",
  ])
}

resource "google_monitoring_notification_channel" "email" {
  for_each     = local.alerting ? toset(var.alert_emails) : toset([])
  display_name = "${local.name} alerts (${each.value})"
  type         = "email"
  labels = {
    email_address = each.value
  }
  depends_on = [google_project_service.apis]
}

# Blackbox check of the unauthenticated health endpoint from several regions.
resource "google_monitoring_uptime_check_config" "health" {
  count            = local.alerting ? 1 : 0
  display_name     = "${local.name} /health"
  timeout          = "10s"
  period           = "300s"
  selected_regions = ["USA_OREGON", "USA_IOWA", "EUROPE"]

  http_check {
    path         = "/health"
    port         = 443
    use_ssl      = true
    validate_ssl = true
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = replace(google_cloud_run_v2_service.web.uri, "https://", "")
    }
  }
}

resource "google_monitoring_alert_policy" "health_down" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} health check failing"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "CRITICAL"

  documentation {
    content = "`/health` on ${google_cloud_run_v2_service.web.uri} failed from multiple probers. Check Cloud Run revision health and the Cloud SQL instance."
  }

  conditions {
    display_name = "Uptime check failing"
    condition_threshold {
      filter = join(" AND ", [
        "resource.type = \"uptime_url\"",
        "metric.type = \"monitoring.googleapis.com/uptime_check/check_passed\"",
        "metric.label.\"check_id\" = \"${google_monitoring_uptime_check_config.health[0].uptime_check_id}\"",
      ])
      comparison      = "COMPARISON_LT"
      threshold_value = 1
      duration        = "300s"
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_FRACTION_TRUE"
        cross_series_reducer = "REDUCE_MEAN"
        group_by_fields      = ["resource.label.host"]
      }
      trigger {
        count = 1
      }
    }
  }
}

resource "google_monitoring_alert_policy" "server_errors" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} 5xx responses"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "ERROR"

  documentation {
    content = "Cloud Run returned more than ${var.alert_5xx_per_minute} server errors per minute. Inspect the request logs and traces for the affected revision."
  }

  conditions {
    display_name = "5xx request rate"
    condition_threshold {
      filter = join(" AND ", [
        local.run_service_filter,
        "metric.type = \"run.googleapis.com/request_count\"",
        "metric.label.\"response_code_class\" = \"5xx\"",
      ])
      comparison      = "COMPARISON_GT"
      threshold_value = var.alert_5xx_per_minute
      duration        = "0s"
      aggregations {
        alignment_period     = "60s"
        per_series_aligner   = "ALIGN_RATE"
        cross_series_reducer = "REDUCE_SUM"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "latency" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} p95 request latency"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "WARNING"

  documentation {
    content = "95th percentile Cloud Run request latency stayed above ${var.alert_latency_p95_ms} ms for 10 minutes. Check Cloud Trace for slow spans (usually Npgsql) and Cloud SQL load."
  }

  conditions {
    display_name = "p95 latency"
    condition_threshold {
      filter = join(" AND ", [
        local.run_service_filter,
        "metric.type = \"run.googleapis.com/request_latencies\"",
      ])
      comparison      = "COMPARISON_GT"
      threshold_value = var.alert_latency_p95_ms
      duration        = "600s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_PERCENTILE_95"
      }
    }
  }
}

# Serilog writes unhandled exceptions to stderr, so any ERROR-severity container log is worth a look.
resource "google_monitoring_alert_policy" "error_logs" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} application error log"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "WARNING"

  documentation {
    content = "An ERROR-or-worse log entry was written by the Cloud Run service. Open Logs Explorer for the full exception."
  }

  conditions {
    display_name = "Error log entry"
    condition_matched_log {
      filter = join(" AND ", [
        "resource.type = \"cloud_run_revision\"",
        "resource.labels.service_name = \"${google_cloud_run_v2_service.web.name}\"",
        "severity >= ERROR",
      ])
    }
  }

  # Required for log-match conditions; also keeps a crash loop from flooding the mailbox.
  alert_strategy {
    notification_rate_limit {
      period = "900s"
    }
  }
}

resource "google_monitoring_alert_policy" "sql_down" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} Cloud SQL unavailable"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "CRITICAL"

  documentation {
    content = "Cloud SQL instance ${google_sql_database_instance.db.name} reported itself down. Expected while the instance is deliberately paused (activation policy NEVER)."
  }

  conditions {
    display_name = "Instance not up"
    condition_threshold {
      filter = join(" AND ", [
        local.sql_instance_filter,
        "metric.type = \"cloudsql.googleapis.com/database/up\"",
      ])
      comparison      = "COMPARISON_LT"
      threshold_value = 1
      duration        = "300s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_MEAN"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "sql_cpu" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} Cloud SQL CPU high"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "WARNING"

  documentation {
    content = "Cloud SQL CPU utilization stayed above ${var.alert_sql_cpu_utilization * 100}% for 15 minutes. Consider a larger tier or check Query Insights."
  }

  conditions {
    display_name = "CPU utilization"
    condition_threshold {
      filter = join(" AND ", [
        local.sql_instance_filter,
        "metric.type = \"cloudsql.googleapis.com/database/cpu/utilization\"",
      ])
      comparison      = "COMPARISON_GT"
      threshold_value = var.alert_sql_cpu_utilization
      duration        = "900s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_MEAN"
      }
    }
  }
}

resource "google_monitoring_alert_policy" "sql_disk" {
  count                 = local.alerting ? 1 : 0
  display_name          = "${local.name} Cloud SQL disk high"
  combiner              = "OR"
  notification_channels = local.alert_channels
  severity              = "WARNING"

  documentation {
    content = "Cloud SQL disk utilization is above ${var.alert_sql_disk_utilization * 100}%. Autoresize is on, so this usually means unexpected growth (large actuals imports or audit rows)."
  }

  conditions {
    display_name = "Disk utilization"
    condition_threshold {
      filter = join(" AND ", [
        local.sql_instance_filter,
        "metric.type = \"cloudsql.googleapis.com/database/disk/utilization\"",
      ])
      comparison      = "COMPARISON_GT"
      threshold_value = var.alert_sql_disk_utilization
      duration        = "900s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_MEAN"
      }
    }
  }
}
