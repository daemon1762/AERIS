# AERIS v0.18.0.0 DEV CP2 Terrain Supply Hotfix 3

## 目的

Render Hotfix 2は黒い三角形・孤立tileによる破損表示を防止したが、実KSPログと動画ではTerrain Tile供給が表示に追いつかなかった。

実測症状：

- Flight進入から最初のtileまで約21秒
- pending最大192、終了時189
- request drop 125
- sampling remaining最大1089
- tile完成間隔最大約92秒
- terrain result age最大約105秒
- GPU failure 0、GPU upload／mesh／contour／worker queueは低負荷

したがって原因はGPU描画ではなく、main-thread PQS samplingへ古い要求が蓄積し、33×33 detail tileを逐次完成させてから表示していた供給設計にあった。

## 修正

### 1. 最新viewportだけを保持

要求を次のlaneへ分離した。

1. `Viewport`
2. `Landing`
3. `LookAhead`
4. `Background`

range、PLAN中心、機体位置または表示世代が変わるたびに最新のdesired setを作り、旧planにしか存在しないqueued／disk-load／samplingを取消す。要求は追加し続けず、最新planの上限内へ収束する。

### 2. 完全な重複排除

同じtileが、

- sampling中
- disk load中
- queue内

のいずれかに存在する場合、新規要求を追加せず、lane、priority、visibility、view generationだけをmergeする。

### 3. Preview→Finalの二段階生成

物理tile範囲は変えず、最初に低解像度previewを生成する。

- Global：5×5（25 sample）
- Far／Route：7×7（49 sample）
- Local／LAND：9×9（81 sample）
- Final：Global 17×17、その他33×33

最大previewでも81 sampleであり、通常final 1089 sampleの10分の1未満。全visible previewを先に供給し、その後にfinal detailへ置換する。PreviewはRAM／GPU表示専用で、Disk Cacheへ保存しない。

### 4. PQS samplingの時間上限

PQSはUnity／KSP main-thread APIのためworkerへ移さない。代わりに各frameで、

- queries-per-second token
- maximum samples per frame
- 実測per-sample EMA
- main-thread stopwatch budget

の小さい値を上限にして小分け供給する。

| Quality | QPS | Samples/frame | Main-thread budget |
|---|---:|---:|---:|
| ECO | 120 | 6 | 0.35 ms |
| BALANCED | 360 | 16 | 0.75 ms |
| HIGH | 720 | 32 | 1.25 ms |
| ULTRA | 1200 | 48 | 1.80 ms |

負荷が高い場合は既存Automatic quality controllerがprofileを下げる。固定budgetを超えて一気にPQSへ問い合わせない。

### 5. Progressive GPU overlay

CPU terrain／既存fallbackを先に描き、準備できたHD tileだけを透明RenderTextureで重ねる。

- coverage 0%：CPU fallbackのみ
- coverage 1～99%：部分HD＋`HD TERRAIN BUILD xx%`
- coverage 100%：HD layer完成

未取得領域は透明なので、黒い三角形へ戻らない。1枚のpreviewから段階的に表示でき、全coverage完成を待たない。

### 6. Preview shading補正

PreviewはFinalと同じ物理範囲を少ない格子で表す。斜面陰影のcell sizeを格子間隔比で補正し、5×5／7×7 previewだけ陰影が過剰になる問題を防止した。

### 7. Telemetry

Performance CSVへ追加した主要列：

```text
terrain_tile_preview_generated
terrain_tile_final_generated
terrain_tile_obsolete_cancelled
terrain_tile_desired
terrain_tile_visible
terrain_tile_preview_count
terrain_tile_sample_batch
terrain_tile_sample_batch_ms
terrain_tile_pqs_sample_ema_ms
terrain_gpu_coverage
```

`terrain_tile_pending`はdisk load中の同一requestを二重計上せず、unique tile workとして記録する。

## 安全境界

- AP／BANK変更なし
- LAND制御権限なし
- 旧NAV不在
- 新NAV BLOCKED
- PQS samplingはmain threadで時間制限
- 補間、GPU mesh、contour、圧縮、disk I/Oは既存共有Scheduler
- ND専用ThreadPool／Task.Runなし
- Safety／LAND worker laneを使用しない
- Preview／GPU結果をProtectの唯一の根拠にしない

## 実KSPで未確認

assistant環境ではxbuild、KSP 1.12.5、Unity GPU描画を実行できない。次をユーザー実機ゲートとする。

- ネイティブC# compile
- 最初のpreview到達時間
- range／PLAN連続操作後のpending収束
- 部分HD表示の時間的安定性
- AMD RADV／DXVKでの透明RenderTexture
- Disk hit後のFinal置換
- 長時間時のRAM／VRAM／request boundedness
