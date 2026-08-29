resource "google_cloud_run_v2_service" "this" {
  project              = var.project_id
  location             = var.region
  name                 = var.name
  deletion_protection  = false
  invoker_iam_disabled = true
  ingress              = "INGRESS_TRAFFIC_ALL"

  template {
    service_account                  = var.service_account
    timeout                          = var.timeout
    max_instance_request_concurrency = var.container_concurrency
    scaling {
      min_instance_count = var.min_instance_count
      max_instance_count = var.max_instance_count
    }
    containers {
      image = var.image
      resources {
        limits   = { cpu = var.cpu, memory = var.memory }
        cpu_idle = true
      }
      ports { container_port = 8080 }
      startup_probe {
        initial_delay_seconds = 2
        timeout_seconds       = 3
        period_seconds        = 5
        failure_threshold     = 12
        http_get { path = "/api/health" }
      }
      dynamic "env" {
        for_each = var.environment
        content {
          name  = env.key
          value = env.value
        }
      }
      dynamic "env" {
        for_each = var.secrets
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value.secret
              version = env.value.version
            }
          }
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].containers[0].image]
  }
}
