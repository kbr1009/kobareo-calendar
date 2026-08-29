terraform {
  backend "gcs" {
    bucket = "kobareo-calendar-tfstate"
    prefix = "bootstrap/identity"
  }
}
