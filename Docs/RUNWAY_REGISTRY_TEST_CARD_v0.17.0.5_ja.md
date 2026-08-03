# v0.17.0.5 実KSP試験カード

## 目的

- 再起動間Provider identity signature決定性
- runtime `geometrySignature`分離
- canonical input fingerprintによる再起動後exact cache hit
- cache schema 4完全往復と自動Archiveの回帰

## 事前条件

現在のcacheは削除しない。v0.17.0.4のalgorithmVersion 1640から1650への安全な再測量を確認する。

## 1回目のKSP起動

1. Flightシーンへ入り、同一Active Vesselをunpack状態で維持する。
2. STARTUP完了後、Manual再走査を2回行う。
3. 各回COMPLETEまで待つ。
4. LAND ARM、AP、NAVは使用しない。

各走査で記録:

```text
records
runways
signature
geometrySignature
REGISTERED / CERTIFIED / FAILED / PENDING / REVALIDATE
MEASURED / CACHE
```

algorithmVersion更新後なので、1回目STARTUPの再測量は正常。Manual 1／2では同一プロセスcache hitが必要。

cache必須ログ:

```text
[AIRFIELD_CACHE] load accepted
[AIRFIELD_CACHE] save verified; ... fullRoundTrip=True
```

出現禁止:

```text
CACHE ROOT MISSING
CACHE LOAD FAILED
CACHE SAVE FAILED
temporary cache full round-trip failed
STAGED DATABASE INVALID
DISC_STOCK_KSP
```

## Archive

Flight終了後、Main Menuで次まで待つ。

```text
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
```

生成ZIPへ`unzip -t`を実行し、検証成功後だけrawフォルダが削除されていることを確認する。

## 2回目のKSP起動

同じGameData・同じ機体で再起動し、STARTUPとManual再走査を1回行う。

合格条件:

- 1回目と2回目の`signature`が完全一致
- records／runwaysが一致
- 登録／認証／失敗／保留／再認証件数が一致
- schema 4 cache load accepted
- 2回目STARTUPでexact cache hitが発生
- 通常想定は`MEASURED 0 / CACHE 16`
- cache save `fullRoundTrip=True`
- 自動archiveが再度完走

`geometrySignature`は診断値として比較する。これだけが変化しても、identity signature・canonical cache hit・最終DBが一致する場合は単独で不合格にしない。

## 提出物

- `GameData/AERISFlightControl`全体のZIP
- ZIP SHA-256
- `KSP.log`
- 可能な範囲の動画
