# Cloud Run configuration

本番Cloud Run構成は`infra/environments/production`、image digestは`.github/workflows/deploy-production.yml`が管理する。旧分割service YAMLはモノリス化に伴い廃止した。

構築と運用は`docs/google-cloud-architecture.md`、`docs/deployment.md`、`docs/database-migrations.md`を参照する。
