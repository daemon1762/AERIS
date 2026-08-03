# AERIS v0.18.0.0 DEV CP2 Runway Map Lock Hotfix 2 + Preload Fast Path 1

## 目的

1. TRACK UP旋回中に滑走路記号とGPU地形の相対位置が滑る再発不具合を根本修正する。
2. Preload BuilderのPQS生成・Chunk保存・再読込みを高速化する。
3. AP、BANK、HDG、PITCH、V/S、ALT、ACC、VEL、Ground StabilityおよびLAND操縦権限には変更を加えない。

## Runway Map Lock Hotfix 2

NDは横方向を`range × 1.30`、縦方向を`range × 1.00`で表示する。旧GPU地形は、この異方性スケール後の正規化座標を通常の回転行列で回していた。一方、滑走路GUIはメートル空間で回転してから横・縦別々に正規化していたため、TRACK UPの方位変化に応じて両者が一致しなかった。

本版では`AERISNdMapProjection`を導入し、以下を一つの不変変換へ統一した。

```text
緯度・経度
→ 球面接平面上のEast/North [m]
→ heading回転 [m]
→ 横1.30／縦1.00のNDスケール
→ RenderTextureまたはGUI座標
```

GPU地形のN-UPキャッシュを再利用する場合は、異方性を補正した行列を使用する。

```text
x' = cosθ·x - sinθ·(V/H)·y
y' = sinθ·(H/V)·x + cosθ·y
```

地形、海岸線、等高線、滑走路端点、滑走路選択判定、LOC平面表示は同じ投影基盤を使用する。

選択滑走路がある場合、GUI経路とGPU経路の端点誤差を毎描画で比較する。

```text
runwayMapLockErrorPx <= 1.00 px
```

1pxを超える場合はGPU地形Commitを拒否し、位置が一致したCPU fallbackを残す。

## Preload Fast Path 1

### 速度プロファイル

```text
BALANCED
→ 従来相当、最大約1.8ms/frame

FAST
→ 無操作時最大約4ms/frame

MAXIMUM
→ 手動選択時、無操作中最大約8ms/frame
```

実測したPQS 1 sample当たり時間から、1 frame当たりsample数を動的計算する。入力再開、フレーム悪化、ND負荷、Worker backlog時は既存Performance Policyに従って即時後退する。

### Preload Builder Final-only Commit

Flight不足Tileは従来どおり25%刻みのProgressive Commitを維持する。非Flight Preload Builderは途中表示を必要としないため、25/50/75%の配列複製を省略し、Final完成時だけTileを生成する。

### 有界Boundary Sample Cache

隣接Tileの境界、共有角、互換LODで緯度・経度が完全一致する地点のPQS結果を再利用する。Keyには天体名、天体半径、環境Hash、緯度・経度を含め、異なる天体・地形構成の値を混在させない。

```text
最大保持数：131,072 samples
対象：Tile境界sampleのみ
Cache hit：PQS query tokenを消費しない
Eviction：有界FIFO
```

内部Sampleは再利用対象にせず、メモリ上限と照合コストを限定する。Builder停止・破棄時にCacheを消去する。

### Chunk書込みBatch

同じ8×8 Chunkへ完成したTileを次の条件まで集約する。

```text
8 Tile到達
または
最初のTileから350ms経過
または
Flight移行・終了Flush
```

一つのBatchにつき、既存Chunk読込み、Chunk全体書込み、round-trip検証、manifest保存を一度だけ実行する。Tileごとのmanifest更新は禁止する。

### Parsed Chunk Cache

最近解析したChunkを最大64MiBまでRAM保持する。同じ地域のRange往復、旋回、連続Tile保存では、Chunkファイルの再読込みと再解析を避ける。

### 追加テレメトリ

```text
runway_map_lock_error_px（NDログ内）
preload_builder_pqs_samples_per_sec
preload_builder_pqs_sample_cost_ms
preload_builder_pqs_sample_cache_hits
preload_builder_pqs_sample_cache_misses
preload_builder_pqs_sample_cache_hit_ratio
preload_builder_chunk_batch_tiles
preload_builder_chunk_rewrite_amplification
preload_builder_chunk_flush_ms
preload_builder_intermediate_commits_skipped
terrain_db_parsed_chunk_cache_hits
terrain_db_parsed_chunk_cache_misses
terrain_db_parsed_chunk_cache_hit_ratio
```

## 安全境界

- ND／Terrain／Preload／UI以外の操縦制御コードは凍結する。
- Preload BuilderはFlight開始時にPQS全球生成を停止する。
- Flight CRITICAL READはArchiveCompression書込みより優先する。
- Chunk保存はJournal、temporary、round-trip CRC、atomic replace、manifestの順を維持する。
- Native KSP実機試験が完了するまでCP2はOPEN、CP3 Gate 6–8はBLOCKEDとする。
