# Bootstrap

Bootstrapはproduction Terraformから分離し、プロジェクト所有者がCloud Shellで段階ごとに実行する。CodexやGitHub Actionsからapplyしない。各段階は独立rootであり、`-target`を使用しない。

## Stage 0: API

Terraformでは管理しない。対象projectを確認後、必要なAPIだけを`gcloud services enable`で有効化する。実行前にサービス一覧と課金影響を再確認する。

## Stage 2: state

`02-state`を最初はlocal backendで適用し、非公開state bucketと保存plan bucketを作る。作成後、backend設定を使って`terraform init -migrate-state`し、ローカルstateが残っていないことを確認する。`terraform.tfstate`をGitへ追加しない。

## Stage 3: identity

`03-identity`はGCSの`bootstrap/identity` prefixを使用する。WIF、サービスアカウント、カスタムロール、IAM、Secretコンテナだけを管理する。Secret値は管理しない。

`terraform-plan`はクラウドリソースに対してread-onlyだが、GCS backendのlockingに必要な`roles/storage.objectAdmin`をstate bucketで持つ。このためstateの改変・削除が技術的に可能であり、完全なread-onlyではない。

`kobareoTerraformApply`の`run.services.setIamPolicy`は、プロジェクト内のCloud Runサービスに作用し得る強い権限である。Project IAMの`resourcemanager.projects.setIamPolicy`は含まれず、自身のIAM、WIF、カスタムロール、APIを変更できない。

## Stage 4: domain

`04-domain`はCloud RunアプリService作成後だけ実行する。stateはローカルへ保存せず、最初からGCS backendの`bootstrap/domain`専用prefixを使用する。
