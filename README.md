# kobareo-calendar

ユーザー、会議室、備品を横軸時間のタイムラインで管理するモノリスWeb予定表です。ASP.NET CoreがReact + TypeScriptの画面、API、WebSocket、認証を同一プロセス・同一オリジンで配信します。

画面はMicrosoft Fluent UI React v9を使ったOutlook風の日表示です。予定本体をドラッグすると30分単位で移動し、左右端のハンドルをドラッグすると開始・終了時刻を変更できます。表示対象ペインは左上のメニューボタンで折りたためます。

## Docker Composeで実行

```bash
cp .env.example .env
docker compose up --build
```

ブラウザで <http://localhost:8080> を開きます。開発用ログインは `admin@example.com` / `ChangeMe123!` です。停止は `docker compose down`、ログを確認する場合は `docker compose logs -f` を使用します。

認証、ユーザー、リソース、予定、表示設定はSQLiteへ保存され、Composeの名前付きボリューム `kobareo-calendar-data` に永続化されます。`docker compose down --volumes` はDBとData Protection鍵を削除するため、意図的な初期化時だけ実行してください。

## 構成と採用理由

- Webアプリ: .NET 10、ASP.NET Core Minimal API、EF Core、IdentityとReact 19.2、TypeScript、Vite、Tailwind CSS v4。React成果物をASP.NET Coreへ組み込み、1つのコンテナとして実行します。
- 層構成: Domain、Application、Infrastructure、WebApp。時間範囲、競合、秘匿などの規則をHTTPやDBから分離しています。
- 開発DB: SQLite。本番DB: SQL Server。`DATABASE_PROVIDER` と接続文字列で切り替えます。
- 認証: `HttpOnly` Cookie。ブラウザへ認証トークンを公開せず、更新APIをCSRFトークンで保護します。

ER図、API方針、削除規則、競合制御は [設計書](docs/design.md) を参照してください。OpenAPI文書は起動後の `/api/openapi/v1.json` から取得できます。

## 設定

| 環境変数 | 用途 | 開発既定値 |
|---|---|---|
| `APP_PORT` | Composeで公開するポート | `8080` |
| `PORT` | コンテナの待受ポート。Cloud Runが注入 | `8080` |
| `DATABASE_PROVIDER` | `Sqlite` または `SqlServer` | `Sqlite` |
| `ConnectionStrings__Default` | DB接続文字列 | `/data/kobareo-calendar.db` |
| `DATA_PROTECTION_PATH` | Cookie暗号鍵の保存先 | `/data/keys` |
| `COOKIE_SECURE` | CookieのHTTPS限定 | Composeでは `false` |
| `TRUST_FORWARD_HEADERS` | 信頼済みプロキシ配下で転送ヘッダーを使用 | `false` |
| `SEED_ADMIN_EMAIL` | 初期管理者メール | `admin@example.com` |
| `SEED_ADMIN_PASSWORD` | 初期管理者パスワード | 開発値のみ |

本番では `COOKIE_SECURE=true` とし、接続文字列、初期パスワードをSecret Manager等から注入します。秘密情報を `.env`、イメージ、ソース管理へ含めないでください。

## DBとマイグレーション

ComposeのSQLiteではアプリ起動時にEF Coreマイグレーションを適用します。本番SQL Serverではアプリ起動時のmigrationを無効化し、専用Cloud Run Jobをサービス更新前に実行します。

```bash
dotnet ef migrations add <名前> \
  --project app/src/KobareoCalendar.Infrastructure \
  --startup-project app/src/KobareoCalendar.WebApp \
  --output-dir Migrations

dotnet ef database update \
  --project app/src/KobareoCalendar.Infrastructure \
  --startup-project app/src/KobareoCalendar.WebApp
```

SQL Server migrationは`KobareoCalendar.SqlServerMigrations`、実行entrypointは`KobareoCalendar.Migration`へ分離します。アプリの常用DBユーザーへDDL権限を与えず、移行ジョブと実行ユーザーを分離してください。詳細は[DB migration運用](docs/database-migrations.md)を参照してください。

バックアップはSQL Serverのマネージドバックアップを使用し、暗号化、保持期間、別障害ドメインへの保管を設定します。復元先へバックアップを戻し、マイグレーション履歴、件数、ログイン、予定取得を確認する復元演習を定期実施してください。RPO/RTOは組織の要件として別途決定します。

## テスト

Composeのイメージビルド時にも.NETとReactのテストが実行されます。個別実行は次のとおりです。

```bash
dotnet test app/KobareoCalendar.slnx -m:1
cd app/src/KobareoCalendar.WebApp/ClientApp
npm install
npm test
npm run build
```

ユニットテストでは日時検証、競合境界、明示承認、非公開秘匿、管理規則、レーン割り当てを検証します。API統合テストでは実際のCookie・CSRF・SQLiteを使い、ロール認可、IDOR防止、非公開応答、競合登録、無効対象拒否を検証します。

## セキュリティ

- セッションCookieは `HttpOnly`、`SameSite=Strict`、本番では `Secure` と `__Host-` 接頭辞を使用します。
- GET以外のAPIはASP.NET Core Antiforgeryで保護します。
- ログインは1 IPあたり毎分10回に制限し、5回失敗で15分間アカウントをロックします。
- 管理APIはAdminロール、予定更新・削除は作成者またはAdminをAPIで検証します。
- 非公開の題名・詳細はAPI DTO生成時に秘匿します。Reactは値をHTMLとして直接挿入しません。
- CSP、クリックジャッキング防止、MIME sniffing防止、Referrer/Permissions Policyを返します。
- CORSは有効化せず、同一オリジンの `/api` プロキシだけを利用します。
- Problem Detailsは本番でスタックトレースを返しません。パスワード、Cookie、非公開内容を明示的にログ出力しません。

## Cloud Run

[Google Cloud構成](docs/google-cloud-architecture.md)と[デプロイ手順](docs/deployment.md)を参照してください。Terraformはインフラ構成、`github-deployer`はimage digestを管理します。

## 既知の制約

- SQL Server用マイグレーション成果物とSQL Server統合テストは、実際の対象SQL Serverバージョンを決定してから作成・検証する必要があります。
- Cloud Runで複数アプリインスタンスを使う前に、Data Protection鍵とWebSocket接続状態を共有ストアへ移してください。現在の構成は最大1インスタンスで、再デプロイ時に再ログインが必要です。
- ブラウザ自動操作のPlaywright E2Eは未導入です。主要なサーバー側フローはHTTP統合テストで検証しています。
- 組織・部署単位の閲覧制限、監査ログ、通知、変更履歴、パスワード再設定メールはMVP対象外です。
