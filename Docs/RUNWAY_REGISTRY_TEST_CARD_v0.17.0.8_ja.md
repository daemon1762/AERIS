# v0.17.0.8 滑走路レジストリ実機試験カード

## 前提

- v0.17.0.7の`PluginData/AirfieldCertificationCache.cfg`と`.bak`を削除しない。
- LAND ARM、AP、旧NAV、新NAVは使用しない。
- KSPは通常終了する。

## 1回目のKSP起動

1. Flightシーンへ入りAIRFIELDSを開く。
2. STARTUP完了後、Manual Reloadを2回実行する。
3. Main Menuへ戻り、自動FlightData ZIP完了後に同じ機体へ再入場する。
4. Flightシーン再生成後にManual Reloadを1回実行する。
5. Main Menuへ戻り、Archive完了後にKSPを終了する。

### 必須ログ

```text
[AIRFIELD_CACHE] canonical identity migration compacted 0 certified alias(es) and 4 failure alias(es).
[AIRFIELD_CACHE] load accepted; certified=16; failures=51.
[AIRFIELD_CACHE] save verified; certified=16; failures=51; fullRoundTrip=True.
```

全走査で次を要求する。

```text
records=157; runways=67; signature=320C1BCE0E271905
REGISTERED 43 RWY / 86 APP
CERTIFIED 13 RWY / 24 APP
FAILED 32 RWY / 62 APP
MEASURED 0 / CACHE 16
```

## 2回目のKSP起動

STARTUPとManual Reloadを1回実行する。

- schema 6を直接load accepted
- failure count 51のまま
- alias compaction追加なし、または0/0
- STARTUP/Manualとも`MEASURED 0 / CACHE 16`
- Provider signatureとDB件数が1回目と一致

## Archive

```text
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
sourceDeleted=True
```

## 即時FAIL

```text
DUPLICATE AIRFIELD
STAGED DATABASE INVALID
CACHE ROOT MISSING
CACHE LOAD FAILED
CACHE SAVE FAILED
full round-trip failed
archive_failed > 0
二回目起動でfailures > 51
MEASURED > 0（初回schema移行を含む）
```

## 提出

`GameData/AERISFlightControl`全体をZIP化し、SHA256を添付する。
