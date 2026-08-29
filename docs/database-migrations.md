# SQL Server migrations

`AddNotificationsAndTargetVisibility` migrationで、通知テーブルと表示対象の表示状態を追加する。SQLite版とSQL Server版をそれぞれのmigration assemblyに保持する。既存の表示対象は`IsVisible = true`で移行し、現在の表示状態を維持する。

SQLite migrationはローカルCompose用として`KobareoCalendar.Infrastructure`に保持する。SQL Server migrationは`KobareoCalendar.SqlServerMigrations`へ分離し、production APIの起動時には適用しない。

`KobareoCalendar.Migration`を`app/Dockerfile`の`migration` targetとしてpublishする。deploy workflowはmigration imageをdigestで更新しCloud Run Jobをtask 1/retry 0で実行する。成功した場合だけモノリスアプリimageを更新する。未適用migrationがなければEF Core処理はno-opである。

通常アプリDBユーザーはDML権限、migration DBユーザーは必要なDDL権限だけを持つ。破壊的migrationは自動実行せず、バックアップ、復元試験、前方互換性、旧revisionへの影響を確認して個別承認を得る。
