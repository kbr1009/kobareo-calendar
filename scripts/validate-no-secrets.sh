#!/usr/bin/env bash
set -euo pipefail

if git ls-files | rg '(^|/)(\.env|.*\.tfvars|.*\.tfstate|.*\.tfplan|gha-creds-.*\.json|application_default_credentials\.json)$' | rg -v '(\.env\.example|\.tfvars\.example)$'; then
  echo "a generated credential, state, plan, or secret values file is tracked" >&2
  exit 1
fi

if git grep -nE -- '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'; then
  echo "a private key marker is tracked" >&2
  exit 1
fi
