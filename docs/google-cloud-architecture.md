# Google Cloud architecture

対象はproject `kobareo-calendar`、region `asia-northeast1`、productionのみ、URLは`https://calendar.kobareo.com`である。React、API、WebSocket、認証を含むモノリスアプリを1つのCloud Run Serviceへ配置し、migrationだけを独立Cloud Run Jobにする。外部SQL ServerへPublic IPで暗号化接続し、接続元IP制限がないためCloud NATや固定IPは作らない。

アプリServiceは`invoker_iam_disabled = true`で公開する。APIはCookie認証、CSRF、ロール/所有者認可で保護する。Migration Jobは非公開である。

アプリはmin 0/max 1、migrationはtask 1/retry 0とする。min 0とmax instance countで費用を制限する。max 1の理由はWebSocket接続一覧とData Protection鍵が共有されていないためであり、可用性上の制約でもある。Billing budgetは通知であり自動停止ではないため、金額決定後にConsoleで設定する。

plan、apply、deploy、各runtime identityを分離し、JSON鍵を作らない。production applyはProject IAM、SA IAM、WIF、カスタムロール、API、DNS、Secret値を変更できない。`run.services.setIamPolicy`はproject内Cloud Runサービスに作用し得るため、保存plan確認とproduction承認を必須とする。

Cloud Run Domain MappingはPreviewで、Googleが本番用途に推奨する方式ではない。推奨されるExternal Application Load Balancerより安価な直接mappingをコスト要件により採用し、制約が問題になった場合はLoad Balancerへ移行する。
