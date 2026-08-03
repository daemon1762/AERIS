# v0.17.0.7 滑走路レジストリ実機試験カード

## 事前条件

- v0.17.0.6の`PluginData/AirfieldCertificationCache.cfg`を削除しない
- LAND ARM、AP、旧NAV関連操作を行わない
- KSPを通常終了する

## 1回目のKSP起動

1. Flightシーンへ入る。
2. STARTUP走査完了まで待つ。
3. Manual再走査を2回行う。
4. 別シーンへ移動後、同じ機体をFlightへ再ロードする。
5. Manual再走査を1回行う。
6. Main Menuへ戻りFlightData ZIP verify完了まで待つ。
7. KSPを通常終了する。

初回STARTUPはalgorithmVersion 1670移行のため`MEASURED 16 / CACHE 0`でよい。その後は次を要求する。

```text
MEASURED 0 / CACHE 16
```

Dull Spot／Mahiでexact keyだけが変化した場合は次の形式を許容する。

```text
[AIRFIELD_CACHE] sub-metre compatible hit
SUB-METRE FRAME COMPATIBLE h=...m v=...m hdg=...deg
```

許容上限:

```text
horizontal <= 0.50 m
vertical   <= 0.10 m
heading    <= 0.02 deg
scale      <= 0.0005
```

## 2回目のKSP起動

1. Flightシーンへ入る。
2. STARTUP走査完了まで待つ。
3. Manual再走査を1回行う。
4. Main Menuへ戻り通常終了する。

STARTUPから次を要求する。

```text
MEASURED 0 / CACHE 16
```

## 全走査共通の合格条件

```text
records=157
runways=67
signature=320C1BCE0E271905
REGISTERED 43 RWY / 86 APP
CERTIFIED 13 RWY / 24 APP
FAILED 32 RWY / 62 APP
PENDING 0 RWY / 0 APP
```

件数またはidentity signatureが変わった場合はHOLD。

## Cache合格条件

```text
schemaVersion = 5
algorithmVersion = 1670
[AIRFIELD_CACHE] load accepted
[AIRFIELD_CACHE] save verified
fullRoundTrip=True
```

次は不合格。

```text
SOURCE ... -> ...
GEOMETRY SHAPE/COUNT CHANGED
PLACEMENT DELTA（上限超過）
CACHE LOAD FAILED
CACHE SAVE FAILED
```

## Archive合格条件

```text
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
sourceDeleted=True
```

## 提出物

- `GameData/AERISFlightControl`フォルダ全体のZIP
- ZIPのSHA-256
- 可能ならKSP.log
- 動画は補助であり必須ではない
