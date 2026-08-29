terraform {
  backend "gcs" {
    bucket = "kobareo-calendar-tfstate"
    prefix = "production/platform"
  }
}
