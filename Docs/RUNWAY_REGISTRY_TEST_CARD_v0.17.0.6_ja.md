# v0.17.0.6 実KSP試験カード

## 目的

- Provider identity signatureの再起動決定性
- Flightシーン再生成後のexact cache hit
- KSP再起動後のexact cache hit
- schema 4完全往復
- 自動FlightData ZIPの回帰防止

## 事前条件

- 現在の`PluginData/AirfieldCertificationCache.cfg`と`.bak`は削除しない
- LAND ARM、AP、旧NAVは使用しない
- KSPは通常終了する

## 一回目のKSP起動

1. Flightへ入りSTARTUP走査完了を待つ
2. AIRFIELDS画面またはログで結果を保存
3. Manual再走査を1回実行
4. Main Menuへ戻り、`ZIP verified`まで待つ
5. 同じKSPプロセスで同じ機体を再度Flightへロード
6. Manual再走査を1回実行
7. Main Menuへ戻り、`ZIP verified`まで待つ
8. KSPを通常終了

algorithmVersionが1650→1660へ変わるため、最初のSTARTUPは次でよい。

```text
MEASURED 16 / CACHE 0
[AIRFIELD_CACHE] exact miss ... reason=ALGORITHM 1650 -> 1660
```

以後は次が必要。

```text
MEASURED 0 / CACHE 16
[AIRFIELD_CACHE] save verified ... fullRoundTrip=True
```

## 二回目のKSP起動

1. Flightへ入りSTARTUP走査完了を待つ
2. Manual再走査を1回実行
3. Main Menuで自動Archive完了を待つ
4. KSPを通常終了

STARTUPから次が必要。

```text
[AIRFIELD_CACHE] load accepted
MEASURED 0 / CACHE 16
```

## 全走査共通の一致条件

```text
Provider records
Provider runways
identity signature
REGISTERED RWY / APP
CERTIFIED RWY / APP
FAILED RWY / APP
PENDING / REVALIDATE
```

`geometrySignature`はruntime診断値なので、identity signatureとDB結果が一致する限り変化しても、このゲート単独ではFAILにしない。

## Archive合格条件

```text
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
sourceDeleted=True
```

## 不合格条件

- 二回目STARTUPが`CACHE 16`未満
- algorithm更新後のManualでfingerprint exact missが再発
- identity signatureが変化
- DB件数が変化
- `CACHE LOAD FAILED`／`CACHE SAVE FAILED`
- `DUPLICATE AIRFIELD`／`STAGED DATABASE INVALID`
- Archive queueが未消化

## 提出物

`GameData/AERISFlightControl`フォルダ全体をZIP化する。動画は任意。AERIS session log、cache、performance CSV、FlightData ZIPがあれば判定可能。
