# kobareo-calendar 作業規約

## 操作対象

- GitHub: `kbr1009/kobareo-calendar`
- Google Cloud project: `kobareo-calendar`
- Google Cloud region: `asia-northeast1`
- Environment: `production`
- Domain: `calendar.kobareo.com`

上記以外のGitHub repository、Google Cloud project、region、environmentを操作しない。値が一致しない場合は停止してユーザーへ確認する。

## 認証と実行境界

- Codex環境でGoogle Cloud Owner認証を行わない。
- サービスアカウントJSON鍵を作成・保存・表示しない。
- Google Cloud認証はGitHub Actions OIDCとWorkload Identity Federationだけを使用する。
- CodexはローカルまたはGitHub Actionsから`terraform apply`、`terraform destroy`を実行しない。
- Codexは`terraform-apply`、`deploy-production`の`workflow_dispatch`、rerun、Environment承認を実行しない。リポジトリ所有者がGitHub画面から手動実行する。
- production applyはProject IAM、サービスアカウントIAM、WIF、カスタムロール、API、Secret値を管理しない。
- bootstrapはユーザーがCloud Shellから段階ごとにplanを確認して適用する。

## GitHub Actions

- 通常PRではクラウド認証を使用しない。
- 認証付きworkflowはactor ID、repository ID、owner ID、workflow ref、branchまたはEnvironmentで制限する。
- fork PRへOIDCトークンやSecretを渡さない。`pull_request_target`を使用しない。
- サードパーティActionは完全なcommit SHAで固定する。
- Required reviewersを利用できない場合、production apply/deployは`push`で自動実行せず、所有者の`workflow_dispatch`だけを許可する。
- 保存planは機密情報として非公開GCS bucketへ短期間だけ保存し、Gitへ追加しない。

## データベースとSecret

- SQLiteはローカル開発専用、SQL Serverはproduction専用とする。
- production API起動時にmigrationを実行しない。専用Cloud Run Jobからのみ実行する。
- DB接続文字列、管理者初期パスワード、Cookie鍵などのSecret値をTerraform、tfvars、state、plan、GitHub、ログへ含めない。
- 破壊的migrationは自動実行せず、DBバックアップと復旧手順を確認してユーザーの明示承認を得る。

## 変更と検証

- ユーザーの既存変更を保持し、変更前後に`git status`と`git diff`を確認する。
- Terraformは`fmt`、`init -backend=false`、`validate`を行う。
- .NET test、React test/build、モノリスDocker buildを可能な範囲で行う。
- commit、push、GitHub/GCP変更は個別承認なしに行わない。
