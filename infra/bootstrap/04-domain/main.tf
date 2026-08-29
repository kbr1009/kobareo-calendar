data "google_project" "current" { project_id = var.project_id }
data "google_cloud_run_v2_service" "app" {
  project  = var.project_id
  location = var.region
  name     = var.app_service_name
}
data "google_dns_managed_zone" "kobareo" {
  project = var.project_id
  name    = var.dns_managed_zone
}

resource "google_cloud_run_domain_mapping" "app" {
  name     = var.domain
  location = var.region
  metadata { namespace = data.google_project.current.project_id }
  spec { route_name = data.google_cloud_run_v2_service.app.name }
  lifecycle { prevent_destroy = true }
}

resource "google_dns_record_set" "calendar" {
  project      = var.project_id
  managed_zone = data.google_dns_managed_zone.kobareo.name
  name         = "${var.domain}."
  type         = "CNAME"
  ttl          = 300
  rrdatas      = ["ghs.googlehosted.com."]
  lifecycle { prevent_destroy = true }
  depends_on = [google_cloud_run_domain_mapping.app]
}
