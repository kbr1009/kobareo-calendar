resource "google_storage_bucket" "state" {
  name                        = var.state_bucket_name
  project                     = var.project_id
  location                    = var.region
  force_destroy               = false
  public_access_prevention    = "enforced"
  uniform_bucket_level_access = true

  versioning { enabled = true }

  soft_delete_policy {
    retention_duration_seconds = 604800
  }

  lifecycle_rule {
    condition { num_newer_versions = 20 }
    action { type = "Delete" }
  }

  lifecycle { prevent_destroy = true }
}

resource "google_storage_bucket" "plans" {
  name                        = var.plan_bucket_name
  project                     = var.project_id
  location                    = var.region
  force_destroy               = false
  public_access_prevention    = "enforced"
  uniform_bucket_level_access = true

  soft_delete_policy {
    retention_duration_seconds = 0
  }

  lifecycle_rule {
    condition { age = 1 }
    action { type = "Delete" }
  }

  lifecycle { prevent_destroy = true }
}
