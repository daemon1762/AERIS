# CP2.5 Candidate 4 Full Boost Backpressure Hotfix 1 実機テストカード

## 再起動回数

KSP起動は1回だけでよい。未構築tileが十分に残る状態で実施する。

## 1. STANDARD基準値 30秒

Main MenuまたはSpace CenterでAERIS Toolbar → Preload Mapsを開く。

記録する項目：

- tiles complete
- pending
- outstanding / limit
- results / required
- required reject / required-drop
- PQS sample/s
- CPU ENCODE
- SSD write MiB/s
- BOTTLENECK

合格：STANDARDでtileまたはblock進捗が継続する。

## 2. FULL BOOST 60秒

`START PRELOAD BOOST — FULL`を押す。

合格：

- `FULL BOOST ACTIVE — BACKPRESSURE GUARDED`
- outstandingは192以下
- required result depthは256未満
- `required-drop 0`
- Scheduler全体のdropが増えても、commit-required dropは0
- tiles completeまたはblock完了が継続増加
- pending固定＋PQS 0＋encode 0＋SSD 0の状態が6秒以上続かない

## 3. 手動STOPとSTANDARD復旧

`STOP PRELOAD BOOST — FULL`を押して10～30秒観察する。

合格：

- STANDARDへ戻る
- 再起動なしでtile／block進捗が続く
- pendingが永久固定しない
- `auto-stop 0`

## 4. 再FULLとFlight safety

FULLを再開始し、Flightへ移動する。

合格ログ：

```text
[PRELOAD_BOOST] state=STOPPED; reason=FLIGHT_SAFETY
```

Flight中にPreload encode／SSD writeが新規開始しない。

## 5. fail-safe確認

通常試験ではfail-safeが発生しないことが合格。もし発生した場合も、次を確認する。

```text
[PRELOAD_BOOST_FAILSAFE]
fallback=STANDARD
databasePayload=UNCHANGED
```

その後、KSP再起動なしでSTANDARD進捗が再開すれば安全境界はPASSだが、FULL性能受入はFAILとしてログを提出する。

## 提出物

- FULL開始前からSTOP後までの動画
- `AERISFlightControl.log`
- `KSP.log`
- `performance_runtime.csv`
