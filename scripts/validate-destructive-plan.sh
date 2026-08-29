#!/usr/bin/env bash
set -euo pipefail

plan_json=${1:?usage: validate-destructive-plan.sh PLAN_JSON}
deletes=$(jq '[.resource_changes[]? | select(.change.actions | index("delete"))] | length' "$plan_json")
if [[ "$deletes" != "0" && "${ALLOW_TERRAFORM_DELETE:-}" != "approved" ]]; then
  echo "the saved plan contains ${deletes} delete or replacement actions; explicit approval is required" >&2
  exit 1
fi
