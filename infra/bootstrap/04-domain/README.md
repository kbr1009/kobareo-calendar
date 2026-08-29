# Domain bootstrap

Cloud Run Domain MappingはPreviewで機能・リージョン制約があり、Googleは本番用途の第一候補として推奨していない。Googleの推奨はGlobal External Application Load Balancerだが、本プロジェクトは費用を抑えるため、制約を受容してDomain Mappingを使用する。

証明書発行の遅延、Preview仕様変更、サービス制約が問題になった場合はExternal Application Load Balancerへ移行する。`04-domain`はCloud Run作成後に独立してplanし、所有者の承認後にCloud Shellから適用する。GCS backendの`bootstrap/domain`を使用し、local stateへフォールバックさせない。
