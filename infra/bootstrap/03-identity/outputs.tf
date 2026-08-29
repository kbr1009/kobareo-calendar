output "workload_identity_providers" {
  value = {
    plan   = google_iam_workload_identity_pool_provider.plan.name
    apply  = google_iam_workload_identity_pool_provider.apply.name
    deploy = google_iam_workload_identity_pool_provider.deploy.name
  }
}

output "service_account_emails" {
  value = { for name, account in google_service_account.account : name => account.email }
}
