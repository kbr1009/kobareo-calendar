resource "google_cloud_run_v2_job" "this" {
  project             = var.project_id
  location            = var.region
  name                = var.name
  deletion_protection = false
  template {
    task_count = 1
    template {
      service_account = var.service_account
      timeout         = "1800s"
      max_retries     = 0
      containers {
        image = var.image
        resources { limits = { cpu = "1", memory = "512Mi" } }
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
  }
  lifecycle { ignore_changes = [template[0].template[0].containers[0].image] }
}

output "name" { value = google_cloud_run_v2_job.this.name }
