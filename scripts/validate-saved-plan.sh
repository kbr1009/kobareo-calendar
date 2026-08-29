#!/usr/bin/env bash
set -euo pipefail

plan=${1:?plan path required}
metadata=${2:?metadata path required}
expected_commit=${3:?expected commit required}
test "$(sha256sum "$plan" | cut -d' ' -f1)" = "$(jq -r .plan_sha256 "$metadata")"
test "$(jq -r .commit_sha "$metadata")" = "$expected_commit"
test "$(jq -r .terraform_version "$metadata")" = "$(terraform version -json | jq -r .terraform_version)"
