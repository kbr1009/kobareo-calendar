# Production deployment

通常PRは無認証で検証する。production infrastructureは手動plan、private GCSでの確認、手動applyの順に進める。アプリdeployも所有者の`workflow_dispatch`だけを許可する。

Cloud Run imageの唯一の管理主体は`github-deployer`である。Terraformは初回image digestを受け取るが、その後はimage fieldを`ignore_changes`する。deployはmigration成功後にモノリスアプリをdigest固定imageで更新し、`latest`を使わない。

初回は循環依存を避けるため、保存planで`deploy_workloads = false`としArtifact Registryだけを構築する。次に`deploy-production`を`bootstrap_images_only = true`で所有者が手動実行し、Cloud Runを更新・実行せずに2つのimageをpushする。workflow summaryのdigestを使って`deploy_workloads = true`の保存planを作り、Cloud Run Serviceとmigration Jobを構築する。`-target`は使用しない。

Required reviewersが利用できる場合もproduction Environmentを使用する。利用できない契約ではmain pushからapply/deployせず、actor IDを固定した手動workflowだけを使用する。Codexはこれらを起動しない。
