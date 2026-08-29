variable "project_id" {
  type    = string
  default = "kobareo-calendar"
  validation {
    condition     = var.project_id == "kobareo-calendar"
    error_message = "Unexpected project."
  }
}
variable "region" {
  type    = string
  default = "asia-northeast1"
  validation {
    condition     = var.region == "asia-northeast1"
    error_message = "Unexpected region."
  }
}
variable "app_service_name" {
  type    = string
  default = "kobareo-calendar"
}
variable "domain" {
  type    = string
  default = "calendar.kobareo.com"
}
variable "dns_managed_zone" {
  description = "Existing Cloud DNS managed zone name for kobareo.com."
  type        = string
}
