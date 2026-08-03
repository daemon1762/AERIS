# CP2 Runway Map Lock Hotfix 2 + Preload Fast Path 1 実機試験カード

対象ZIP：

```text
AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayMapLockHotfix2_PreloadFastPath1_CompileHotfix1_Source.zip
```

## 1. Native build

静的受入が全PASSし、Mono C# buildが成功すること。エラー時はコードを手動修正せず端末出力を保存する。

## 2. Runway Map Lock

録画必須。Island Airfieldまたは輪郭が明確なMOD空港を選択する。

```text
NORTH UPで40km
TRACK UPへ切替
20 → 40 → 80 → 160 → 80 → 40km
左右それぞれ90°以上旋回
Preview → Final置換待ち
TOPO → REL → AUTO
```

合格条件：

- 滑走路両端が実地形上の同じ位置へ固定され続ける。
- 旋回中に滑走路が島・海岸線・山体上を滑らない。
- LOD、Preview／Final、Range変更で跳ばない。
- `[ND/TERRAIN_ALIGN] geometryProjection=SHARED_SCALE_CORRECTED`。
- `runwayMapLockErrorPx <= 1.000`。
- `[ND/RUNWAY_MAP_LOCK] terrain commit rejected`が定常的に発生しない。

## 3. MOD空港LAND表示

KolaIsland RWY 34など認証済みMOD滑走路を選ぶ。

合格条件：

- 正しい進入側でLOC／GS漏斗が滑走路中心線と一致する。
- 反対側だけ`LOC N/A / GS N/A / NOT ON APPROACH SIDE`となる。
- `CERT`、LAND ARM、Geometry状態が矛盾しない。

## 4. Preload速度

Space Center／VAB／SPHで録画とログを取得する。

```text
BALANCED 60秒
FAST 60秒
MAXIMUM 60秒
各区間で30秒放置
その後マウス・キー操作を再開
```

合格条件：

- FAST／MAXIMUMで`preload_builder_pqs_samples_per_sec`がBALANCEDより増える。
- 操作再開時にPQS予算が即時後退し、UI入力が引っ掛からない。
- `preload_builder_chunk_batch_tiles`が1を超える区間がある。
- `preload_builder_intermediate_commits_skipped`が増える。
- 隣接Tile生成後に`preload_builder_pqs_sample_cache_hits`が増える。
- 境界共有が発生する区間で`preload_builder_pqs_sample_cache_hit_ratio`が0より大きくなる。
- Cache hitを含めてもQueueと処理量が無制限増加しない。
- Flightへ入ると`PRELOAD SUSPENDED / FLIGHT READ PRIORITY`となる。

## 5. Parsed Chunk Cache

同じ地域でRangeを往復する。

合格条件：

- `terrain_db_parsed_chunk_cache_hits`が増える。
- hit ratioが0より大きくなる。
- 同一ChunkのDisk readが毎回発生しない。
- 古いTileが後から復活しない。

## 6. 保存と再起動

Preload生成後に通常終了し、KSPを再起動する。

合格条件：

- manifest／Chunk／Journal破損なし。
- 生成済みTileが再利用される。
- CRC failureなし。
- pending markerが残っても非Flight recoveryで安全に収束する。

## 最終判定

Runway Map Lock、LAND表示、Preload速度、保存・再起動がすべてPASSした場合のみCP2 CLOSEを再判定する。
