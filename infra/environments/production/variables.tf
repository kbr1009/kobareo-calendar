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
}
variable "migration_image" {
  description = "Initial immutable migration image digest."
  type        = string
}
variable "app_max_instance_count" {
  type    = number
  default = 1
}
