variable "project_id" {
  type    = string
  default = "kobareo-calendar"
  validation {
    condition     = var.project_id == "kobareo-calendar"
    error_message = "project_id must be kobareo-calendar."
  }
}

variable "region" {
  type    = string
  default = "asia-northeast1"
  validation {
    condition     = var.region == "asia-northeast1"
    error_message = "region must be asia-northeast1."
  }
}

variable "state_bucket_name" {
  type    = string
  default = "kobareo-calendar-tfstate"
}

variable "plan_bucket_name" {
  description = "Globally unique private bucket name for saved production plans."
  type        = string
}
