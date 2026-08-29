#!/usr/bin/env bash
set -euo pipefail

env DATABASE_PROVIDER=SqlServer \
  'ConnectionStrings__Default=Server=localhost;Database=design;Integrated Security=True;Encrypt=False' \
  dotnet ef migrations has-pending-model-changes \
  --project app/src/KobareoCalendar.SqlServerMigrations \
  --startup-project app/src/KobareoCalendar.Migration \
  --context AppDbContext \
  --configuration Release \
  --no-build
