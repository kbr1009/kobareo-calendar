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
variable "repository_id" {
  type    = string
  default = "kobareo-calendar"
}
variable "app_image" {
  description = "Initial immutable monolith application image digest."
  type        = string
  default     = null
  validation {
    condition     = !var.deploy_workloads || can(regex("^asia-northeast1-docker\\.pkg\\.dev/kobareo-calendar/kobareo-calendar/app@sha256:[0-9a-f]{64}$", var.app_image))
    error_message = "app_image must be the immutable production Artifact Registry digest when deploy_workloads is true."
  }
}
variable "migration_image" {
  description = "Initial immutable migration image digest."
  type        = string
  default     = null
  validation {
    condition     = !var.deploy_workloads || can(regex("^asia-northeast1-docker\\.pkg\\.dev/kobareo-calendar/kobareo-calendar/migration@sha256:[0-9a-f]{64}$", var.migration_image))
    error_message = "migration_image must be the immutable production Artifact Registry digest when deploy_workloads is true."
  }
}
variable "deploy_workloads" {
  description = "Create the Cloud Run service and migration job after immutable images exist."
  type        = bool
  default     = true
}
variable "app_max_instance_count" {
  type    = number
  default = 1
}
