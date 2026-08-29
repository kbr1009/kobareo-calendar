# Saved Terraform plan approval

通常PRはクラウド認証を行わない。所有者が`terraform-plan`をGitHub画面から手動起動し、main HEADのcommit SHAとimmutable image digestを入力する。

保存plan、checksum、metadata、表示用テキストはPublic Access Preventionを有効にした専用GCS bucketへ24時間だけ保存する。publicなActionsログへplan全文を出力しない。

`terraform-plan` SAはこの専用bucketに限り`storage.objects.create`、`storage.objects.get`、`storage.objects.list`を持つ。`get`と`list`は`gcloud storage cp`のアップロード先検証に必要であり、バケット内のオブジェクト名の一覧と保存planの読み取りが可能になるリスクを伴う。更新、削除は許可しない。

applyは人間が確認した同じbinary planだけを使用し再planしない。SHA-256、commit SHA、Terraform 1.10.5、lockfile SHA、workflow run IDを検証する。Required reviewersが使えない場合はmain pushでapplyせず、所有者が`workflow_dispatch`し、run ID、SHA、確認文字列を入力する。Codexはplan/apply/deploy workflowの起動、rerun、Environment承認を行わない。
