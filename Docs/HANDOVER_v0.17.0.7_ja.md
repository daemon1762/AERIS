# AERIS v0.17.0.7 引き継ぎ

## 正式ベース

v0.17.0.6からの限定ホットフィックス。変更対象は滑走路認証cache compatibilityだけ。

## 実機で確認されたv0.17.0.6結果

- STARTUP: `MEASURED 16 / CACHE 0`
- Manual 1: `MEASURED 0 / CACHE 16`
- Manual 2: `MEASURED 0 / CACHE 16`
- Flightシーン再生成後: `MEASURED 2 / CACHE 14`
- miss: Dull Spot Runway、Mahi Runway
- Provider identity signature: `320C1BCE0E271905`で安定
- DB: `43 RWY / 86 APP`, `13 RWY / 24 APP CERTIFIED`
- 自動FlightData ZIP: 2本ともverify・raw削除完走

## v0.17.0.7変更点

- cache schema 5
- algorithmVersion 1670
- `SourceFingerprint`をsnapshot/cacheへ追加
- exact fingerprintとsource fingerprintを分離
- strict sub-metre compatibility gateを追加
- 水平0.50m、垂直0.10m、方位0.02°、scale 0.0005
- source/config/model、Provider version、Geometry件数、Collider状態が一致しない場合は必ず再測量

## 変更禁止

- AERISBankDirector
- AP制御則
- LAND control authority
- 旧NAV削除状態
- archive integrity contract

## 次の実機ゲート

初回は1660→1670移行のため再測量を許容する。Manual 2回、Flightシーン再生成後Manual、KSP再起動後STARTUPで`MEASURED 0 / CACHE 16`を要求する。

Dull Spot／Mahiでは`[AIRFIELD_CACHE] sub-metre compatible hit`が記録されてよい。他施設で同ログが出た場合も、差分値とsource hashを確認する。
