# Rollback

アプリだけの障害は、直前に正常だったArtifact Registry digestを指定してモノリスアプリrevisionを更新する。Terraform構成の障害は修正PRから新しいplanを作成し、stateを手動編集しない。

DB schemaはCloud Run revisionのrollbackでは戻らない。migration失敗時はサービスを更新せず、SQL ServerバックアップとDBA承認済み手順で復元または前進migrationする。`terraform destroy`、`terraform state push`、`force-unlock`は通常復旧に使用しない。
