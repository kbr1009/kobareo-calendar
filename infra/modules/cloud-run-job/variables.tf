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
