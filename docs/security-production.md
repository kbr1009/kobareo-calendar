# Production security

WebAppはASP.NET Core IdentityのHttpOnly、Secure、SameSite=Strict Cookieを使用し、更新APIはAntiforgery tokenでCSRFを検証する。ログイン失敗は5回で15分lockout、ログインendpointは接続元IPごとに毎分10回へ制限する。

管理APIはAdmin roleを要求する。予定の更新・削除は管理者または予定作成者だけを許可し、UIだけでなくAPIでも拒否する。非公開予定はDTO生成時に秘匿する。

CORS middlewareは有効化しない。React、`/api`、WebSocketはASP.NET Coreから同一オリジンで配信し、他オリジンへ資格情報付きCORSを許可しない。`TRUST_FORWARD_HEADERS=true`はCloud Run proxy配下だけで使用する。

一般APIは認証、Cloud Run concurrency 40、app max instance 1で上限を設ける。公開後のアクセス特性、429率、5xx率を確認してendpoint別制限を追加する。

アプリDB接続とmigration DB接続を分離する。Secret値はSecret Managerへ所有者が手動登録し、Terraform、tfvars、state、plan、GitHubへ含めない。

WIF診断はOIDC tokenをrunnerのメモリ内でだけ解析する。token全体、audience、GitHubのOIDC request token、Google credential fileはログやJob Summaryへ出力しない。出力対象はactor ID、repository/owner ID、subject、workflow ref、Git ref、Environmentだけとする。通常のapply/deployでは実際のOIDC claimが期待するWIF境界と一致しない限りGoogle認証へ進まない。

app max 1、min 0とし、migrationは手動production workflowだけで実行する。Artifact Registryは未タグimageを30日後に削除し直近20世代を保持する。Billing budgetと通知channelは金額を所有者が決めるまで未構築である。
