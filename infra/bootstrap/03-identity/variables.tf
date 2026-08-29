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
variable "github_repository" {
  type    = string
  default = "kbr1009/kobareo-calendar"
}
variable "github_repository_id" { type = string }
variable "github_owner_id" { type = string }
variable "github_actor_id" { type = string }
variable "state_bucket_name" {
  type    = string
  default = "kobareo-calendar-tfstate"
}
variable "plan_bucket_name" { type = string }
