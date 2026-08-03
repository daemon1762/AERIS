# AERIS Flight Control v0.17.0.8 リリースノート

## 対象

v0.17.0.7実機試験で確認された、TSC Runway 09/27のfailure hintがKSP再起動ごとに2件ずつ増える問題を修正する。

## 実機証拠

- v0.17.0.7 DLL読込み：PASS
- Provider records/runways：`157 / 67`
- identity signature：`320C1BCE0E271905`で再起動間一致
- 最終DB：`43 RWY / 86 APP`、`13 RWY / 24 APP CERTIFIED`、`32 RWY / 62 APP FAILED`
- 初回Manual 2回：`MEASURED 0 / CACHE 16`
- 二回目STARTUP/Manual：`MEASURED 0 / CACHE 16`
- cache保存：`fullRoundTrip=True`
- 自動Archive：queued→scheduler accepted→ZIP verified→`sourceDeleted=True`
- Writer drop/failure：0

一方、failure cacheは初回終了時53件、二回目終了時55件となった。TSC 09/27の`UniversalSpawnPoint`が起動ごとに異なるProvider UUIDを返し、同一site/path/modelがUUID別のStableRecordIdとして保存されていた。現cacheにはTSC 09/27それぞれ3世代、計6件が存在し、正規化すると2件となるため4件が不要な別名である。

## 変更

- cache schema 6
- StableRecordId生成を`AERISProviderIdentity`へ集約
- site/path/modelがある場合はUUIDをIDから除外
- 安定フィールドがない場合のみUUIDをfallback使用
- IDへsource pathを含める
- schema 5/4/3のcertified/failure recordを読込み時に正規化
- 正規ID衝突時は高いalgorithmVersion、次に新しいsavedUtcを優先
- cache内のrunway/direction StableIdも正規IDへ移行
- 移行圧縮件数をログへ出力

## 変更しないもの

- survey algorithmVersion 1670
- geometry/source fingerprint
- サブメートル互換上限
- AP/BANK制御則
- LAND観測専用境界
- 旧NAV削除状態
- 自動FlightData Archive

## 静的受入

`python3 Tools/run_v01708_acceptance.py`

実KSPコンパイルとschema 5→6移行試験は未実証。
