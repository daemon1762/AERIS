# v0.17.0.4 実KSP試験カード

## 目的

- schema 3 → 4 cache移行
- StableRecordId完全往復
- STARTUP／Manual／再起動間Provider signature決定性
- v0.17.0.3 archive修正の回帰確認

## 1回目の起動

1. Flightシーンへ入り、同一Active Vesselをunpack状態で維持する。
2. STARTUP走査完了まで待つ。
3. Manual再走査を2回行い、各回COMPLETEまで待つ。
4. LAND ARM、AP、NAVは使用しない。

期待ログ:

```text
[AIRFIELD_CACHE] schema-3 migration retained ... certified record(s) and discarded ... ambiguous failure hint(s) for safe rebuild.
[AIRFIELD_PROVIDER_SNAPSHOT] cause=STARTUP ...
[AIRFIELD_CACHE] save verified; ... fullRoundTrip=True.
[AIRFIELD_PROVIDER_SNAPSHOT] cause=MANUAL ...
[AIRFIELD_CACHE] save verified; ... fullRoundTrip=True.
```

STARTUP／Manual 1／Manual 2で以下が完全一致すること。

- records
- runways
- signature
- REGISTERED RWY / APP
- CERTIFIED RWY / APP
- FAILED RWY / APP
- PENDING / REVALIDATE

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

生成ZIPへ`unzip -t`を実行し、rawフォルダが検証成功後だけ削除されていることを確認する。

## 2回目の起動

同じ機体・同じGameDataで再起動し、STARTUPとManual再走査を1回行う。

期待条件:

- schema 4 cache load accepted
- schema-3 migrationログは再出現しない
- 1回目と2回目のProvider signatureが一致
- 登録／認証／失敗／再認証件数が一致
- cache保存が再びfullRoundTrip=True

## 提出物

- `GameData/AERISFlightControl`全体のZIP
- ZIP SHA-256
- `KSP.log`
- 可能な範囲の動画

画面撮影に欠損があっても、AERIS session log、cache実体、FlightData ZIPが揃えば判定可能。
