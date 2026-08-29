data "google_project" "current" { project_id = var.project_id }

locals {
  service_accounts = {
    terraform-plan    = "Read-only cloud plan identity; state locking can write state objects"
    terraform-apply   = "Approved production infrastructure apply identity"
    github-deployer   = "Approved production application deployment identity"
    app-runtime       = "Cloud Run monolith application runtime identity"
    migration-runtime = "Cloud Run database migration runtime identity"
  }
  plan_permissions = [
    "artifactregistry.locations.get", "artifactregistry.locations.list",
    "artifactregistry.repositories.get", "artifactregistry.repositories.list",
    "iam.serviceAccounts.get", "iam.serviceAccounts.list",
    "resourcemanager.projects.get",
    "run.jobs.get", "run.jobs.list", "run.locations.list", "run.operations.get",
    "run.services.get", "run.services.list"
  ]
  apply_permissions = concat(local.plan_permissions, [
    "artifactregistry.repositories.create", "artifactregistry.repositories.delete",
    "artifactregistry.repositories.update",
    "run.jobs.create", "run.jobs.delete", "run.jobs.update",
    "run.services.create", "run.services.delete", "run.services.setIamPolicy",
    "run.services.update"
  ])
  deploy_permissions = [
    "resourcemanager.projects.get", "run.executions.get", "run.executions.list",
    "run.jobs.get", "run.jobs.run", "run.jobs.update", "run.operations.get",
    "run.services.get", "run.services.update"
  ]
}

resource "google_service_account" "account" {
  for_each     = local.service_accounts
  project      = var.project_id
  account_id   = each.key
  display_name = each.value
}

resource "google_project_iam_custom_role" "terraform_plan" {
  project     = var.project_id
  role_id     = "kobareoTerraformPlan"
  title       = "Kobareo Terraform Plan"
  permissions = local.plan_permissions
}

resource "google_project_iam_custom_role" "terraform_apply" {
  project     = var.project_id
  role_id     = "kobareoTerraformApply"
  title       = "Kobareo Terraform Apply"
  permissions = local.apply_permissions
}

resource "google_project_iam_custom_role" "github_deploy" {
  project     = var.project_id
  role_id     = "kobareoGithubDeploy"
  title       = "Kobareo GitHub Deploy"
  permissions = local.deploy_permissions
}

resource "google_project_iam_member" "plan_role" {
  project = var.project_id
  role    = google_project_iam_custom_role.terraform_plan.name
  member  = "serviceAccount:${google_service_account.account["terraform-plan"].email}"
}

resource "google_project_iam_member" "apply_role" {
  project = var.project_id
  role    = google_project_iam_custom_role.terraform_apply.name
  member  = "serviceAccount:${google_service_account.account["terraform-apply"].email}"
}

resource "google_project_iam_member" "deploy_role" {
  project = var.project_id
  role    = google_project_iam_custom_role.github_deploy.name
  member  = "serviceAccount:${google_service_account.account["github-deployer"].email}"
}

resource "google_project_iam_member" "apply_artifact_reader" {
  project = var.project_id
  role    = "roles/artifactregistry.reader"
  member  = "serviceAccount:${google_service_account.account["terraform-apply"].email}"
}

resource "google_project_iam_member" "deploy_artifact_writer" {
  project = var.project_id
  role    = "roles/artifactregistry.writer"
  member  = "serviceAccount:${google_service_account.account["github-deployer"].email}"
}

resource "google_service_account_iam_member" "apply_act_as" {
  for_each           = toset(["app-runtime", "migration-runtime"])
  service_account_id = google_service_account.account[each.key].name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.account["terraform-apply"].email}"
}

resource "google_service_account_iam_member" "deploy_act_as" {
  for_each           = toset(["app-runtime", "migration-runtime"])
  service_account_id = google_service_account.account[each.key].name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.account["github-deployer"].email}"
}

resource "google_storage_bucket_iam_member" "plan_state" {
  bucket = var.state_bucket_name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.account["terraform-plan"].email}"
}

resource "google_storage_bucket_iam_member" "apply_state" {
  bucket = var.state_bucket_name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.account["terraform-apply"].email}"
}

resource "google_storage_bucket_iam_member" "plan_artifact_create" {
  bucket = var.plan_bucket_name
  role   = "roles/storage.objectCreator"
  member = "serviceAccount:${google_service_account.account["terraform-plan"].email}"
}

resource "google_storage_bucket_iam_member" "apply_artifact_read" {
  bucket = var.plan_bucket_name
  role   = "roles/storage.objectViewer"
  member = "serviceAccount:${google_service_account.account["terraform-apply"].email}"
}

resource "google_iam_workload_identity_pool" "github" {
  for_each                  = toset(["plan", "apply", "deploy"])
  project                   = var.project_id
  workload_identity_pool_id = "github-${each.key}"
  display_name              = "GitHub Actions ${each.key}"
  lifecycle { prevent_destroy = true }
}

locals {
  oidc_mapping = {
    "google.subject"          = "assertion.sub"
    "attribute.actor_id"      = "assertion.actor_id"
    "attribute.repository_id" = "assertion.repository_id"
    "attribute.owner_id"      = "assertion.repository_owner_id"
    "attribute.workflow_ref"  = "assertion.workflow_ref"
  }
  base_condition = "assertion.repository_id == '${var.github_repository_id}' && assertion.repository_owner_id == '${var.github_owner_id}' && assertion.actor_id == '${var.github_actor_id}'"
}

resource "google_iam_workload_identity_pool_provider" "plan" {
  project                            = var.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.github["plan"].workload_identity_pool_id
  workload_identity_pool_provider_id = "github-plan"
  attribute_mapping                  = local.oidc_mapping
  attribute_condition                = "${local.base_condition} && assertion.workflow_ref == '${var.github_repository}/.github/workflows/terraform-plan.yml@refs/heads/main'"
  oidc { issuer_uri = "https://token.actions.githubusercontent.com" }
  lifecycle { prevent_destroy = true }
}

resource "google_iam_workload_identity_pool_provider" "apply" {
  project                            = var.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.github["apply"].workload_identity_pool_id
  workload_identity_pool_provider_id = "github-apply"
  attribute_mapping                  = local.oidc_mapping
  attribute_condition                = "${local.base_condition} && assertion.ref == 'refs/heads/main' && assertion.sub == 'repo:${var.github_repository}:environment:production' && assertion.workflow_ref == '${var.github_repository}/.github/workflows/terraform-apply.yml@refs/heads/main'"
  oidc { issuer_uri = "https://token.actions.githubusercontent.com" }
  lifecycle { prevent_destroy = true }
}

resource "google_iam_workload_identity_pool_provider" "deploy" {
  project                            = var.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.github["deploy"].workload_identity_pool_id
  workload_identity_pool_provider_id = "github-deploy"
  attribute_mapping                  = local.oidc_mapping
  attribute_condition                = "${local.base_condition} && assertion.ref == 'refs/heads/main' && assertion.sub == 'repo:${var.github_repository}:environment:production' && assertion.workflow_ref == '${var.github_repository}/.github/workflows/deploy-production.yml@refs/heads/main'"
  oidc { issuer_uri = "https://token.actions.githubusercontent.com" }
  lifecycle { prevent_destroy = true }
}

resource "google_service_account_iam_member" "wif" {
  for_each = {
    plan = {
      service_account = google_service_account.account["terraform-plan"].name
      pool_id         = google_iam_workload_identity_pool.github["plan"].workload_identity_pool_id
    }
    apply = {
      service_account = google_service_account.account["terraform-apply"].name
      pool_id         = google_iam_workload_identity_pool.github["apply"].workload_identity_pool_id
    }
    deploy = {
      service_account = google_service_account.account["github-deployer"].name
      pool_id         = google_iam_workload_identity_pool.github["deploy"].workload_identity_pool_id
    }
  }
  service_account_id = each.value.service_account
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/projects/${data.google_project.current.number}/locations/global/workloadIdentityPools/${each.value.pool_id}/attribute.repository_id/${var.github_repository_id}"
}

resource "google_secret_manager_secret" "secret" {
  for_each  = toset(["app-database-connection", "migration-database-connection", "seed-admin-email", "seed-admin-password"])
  project   = var.project_id
  secret_id = each.key
  replication {
    auto {}
  }
  lifecycle { prevent_destroy = true }
}

resource "google_secret_manager_secret_iam_member" "app" {
  for_each  = toset(["app-database-connection"])
  project   = var.project_id
  secret_id = google_secret_manager_secret.secret[each.key].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.account["app-runtime"].email}"
}

resource "google_secret_manager_secret_iam_member" "migration" {
  for_each  = toset(["migration-database-connection", "seed-admin-email", "seed-admin-password"])
  project   = var.project_id
  secret_id = google_secret_manager_secret.secret[each.key].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.account["migration-runtime"].email}"
}
