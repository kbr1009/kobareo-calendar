locals {
  service_accounts = {
    app       = "app-runtime@${var.project_id}.iam.gserviceaccount.com"
    migration = "migration-runtime@${var.project_id}.iam.gserviceaccount.com"
  }
}

module "registry" {
  source        = "../../modules/artifact-registry"
  project_id    = var.project_id
  region        = var.region
  repository_id = var.repository_id
}

module "app" {
  count                 = var.deploy_workloads ? 1 : 0
  source                = "../../modules/cloud-run-service"
  project_id            = var.project_id
  region                = var.region
  name                  = "kobareo-calendar"
  image                 = var.app_image
  service_account       = local.service_accounts.app
  max_instance_count    = var.app_max_instance_count
  container_concurrency = 40
  environment = {
    ASPNETCORE_ENVIRONMENT    = "Production"
    COOKIE_SECURE             = "true"
    DATABASE_PROVIDER         = "SqlServer"
    DATA_PROTECTION_PATH      = "/tmp/keys"
    RUN_MIGRATIONS_ON_STARTUP = "false"
    TRUST_FORWARD_HEADERS     = "true"
  }
  secrets = {
    ConnectionStrings__Default = { secret = "app-database-connection" }
  }
  depends_on = [module.registry]
}

module "migration" {
  count           = var.deploy_workloads ? 1 : 0
  source          = "../../modules/cloud-run-job"
  project_id      = var.project_id
  region          = var.region
  name            = "kobareo-migration"
  image           = var.migration_image
  service_account = local.service_accounts.migration
  environment = {
    ASPNETCORE_ENVIRONMENT = "Production"
    DATABASE_PROVIDER      = "SqlServer"
  }
  secrets = {
    ConnectionStrings__Default = { secret = "migration-database-connection" }
    SEED_ADMIN_EMAIL           = { secret = "seed-admin-email" }
    SEED_ADMIN_PASSWORD        = { secret = "seed-admin-password" }
  }
  depends_on = [module.registry]
}
