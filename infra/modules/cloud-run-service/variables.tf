variable "project_id" { type = string }
variable "region" { type = string }
variable "name" { type = string }
variable "image" { type = string }
variable "service_account" { type = string }
variable "environment" {
  type    = map(string)
  default = {}
}
variable "secrets" {
  type    = map(object({ secret = string, version = optional(string, "latest") }))
  default = {}
}
variable "cpu" {
  type    = string
  default = "1"
}
variable "memory" {
  type    = string
  default = "512Mi"
}
variable "min_instance_count" {
  type    = number
  default = 0
}
variable "max_instance_count" { type = number }
variable "timeout" {
  type    = string
  default = "3600s"
}
variable "container_concurrency" {
  type    = number
  default = 80
}
