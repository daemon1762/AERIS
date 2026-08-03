# AERIS v0.18.0.0 CP2.5 Candidate 4 Full Boost Backpressure Hotfix 1

## 目的

Candidate 4のFULL BOOSTで、Terrain Block job/resultが共有Scheduler容量を超え、`PendingBlocks` creditが返却されずPreloadが永久停止する不具合を修正する。

実機AERIS34ではFULL開始後にScheduler dropが`0 → 1620`へ増加し、tile完了数が`5148`で停止した。STOP後も標準Preloadへ復旧しなかった。

## 修正契約

### Commit-required Scheduler経路

Terrain Blockは`SubmitRequired`を使用する。

- queue満杯時は既存jobを破棄せず、submitを拒否して呼出側へbackpressureを返す。
- commit-required resultはbest-effort resultより優先する。
- nominal result queueは256。Terrain側のglobal outstanding上限をFULL 192、STANDARD 96に固定し、commit-required resultを256未満へ拘束する。
- worker error／stale terminalでも`Commit(null)`を呼び、所有者creditを必ず終了させる。

### Terrain Block global credit

- STANDARD outstanding block上限：96
- FULL outstanding block上限：192
- per-tile pending上限：STANDARD 4、FULL 12を維持
- completed sample blockはScheduler admissionに成功するまでTileState内に保持し、再サンプリングしない。
- creditはepoch付きleaseで管理し、fail-safe recovery後の旧callbackが新しいcreditを減らさない。

### Fail-safe recovery

次の場合、FULL BOOSTを自動停止してSTANDARDへ戻す。

- commit-required result dropが観測された
- block／tile進捗が6秒停止し、PQS・encode・SSDもidleのままoutstandingが残った

RecoveryではPreload Builder所有の未確定TileStateだけを破棄し、現在天体のscan cursorを先頭へ戻す。保存済みTerrain Database payloadは変更しない。

ログ：

```text
[PRELOAD_BOOST_FAILSAFE] reason=PIPELINE_STALL; recoveredTiles=...; fallback=STANDARD; databasePayload=UNCHANGED
```

### 診断UI

Preload Mapsへ次を表示する。

- outstanding / limit
- scheduler result depth / required result depth
- required submit reject
- required result drop
- admission backpressure count
- recovery count / last recovery reason
- FULL auto-stop count

Bottleneck表示へ追加：

- `RESULT BACKPRESSURE`
- `PIPELINE STALL`

## 非変更範囲

- PQS品質・tile座標・LOD定義
- SSD chunk format／manifest format
- Map DRAM metadata-only境界
- ND／FDI表示policy
- AA／AP／PROTECT／LAND
- 滑走路情報Track B
- FULL BOOSTの手動開始・手動停止、再起動時非復元、Flight safety stop

## 実機合格条件

1. FULL BOOSTを最低60秒動かす。
2. `SchedulerRequiredDropped=0`を維持する。
3. tile完了数またはblock完了数が継続増加する。
4. STOP後10秒以内にSTANDARDで進捗が再開する。
5. fail-safeを意図的に発生させない通常試験では`BoostAutoStops=0`。
6. Flight移行時は従来どおり`FLIGHT_SAFETY`で停止する。
