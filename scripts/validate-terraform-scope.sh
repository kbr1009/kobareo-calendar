#!/usr/bin/env bash
set -euo pipefail

forbidden='google_(project_iam|service_account_iam|iam_workload_identity|project_service|secret_manager_secret_version|dns_)'
if rg -n "$forbidden" infra/environments/production infra/modules; then
  echo "production Terraform contains a forbidden control-plane resource" >&2
  exit 1
fi

if rg -n 'project\s*=\s*"(?!kobareo-calendar)' infra/environments/production infra/modules --pcre2; then
  echo "production Terraform contains an unexpected literal project" >&2
  exit 1
fi

if rg -n 'workloadIdentityPools/.+/attribute/' infra/bootstrap; then
  echo "WIF principalSet attribute selectors must use attribute.NAME syntax" >&2
  exit 1
fi
