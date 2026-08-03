# AERIS v0.18.0.0 DEV CP2 — 統合Preload地形基盤

## 1. 位置付け

本チェックポイントは、改良型Navigation Displayの地形主供給源をFlight中のPQS生成から、非Flight中に育成した**Preload Terrain Database**へ移すためのCP2統合実装である。

正式構成は次の四要素で固定する。

1. Preload Terrain Map Builder
2. Preload Terrain Database
3. Asynchronous Multithreaded Terrain Loading
4. Terrain Block Pipeline

Terrain Block Pipelineは主系統ではなく、DBに存在しない現在必要範囲だけを埋める補完系統である。

## 2. 実行フロー

```text
KSP起動／Space Center／VAB／SPH／Tracking Station／その他非Flight
  ↓
Preload Builderが入力状態とPerformance Runtime負荷を観測
  ↓
PQSをmain-thread小バッチで取得
  ↓
共有GeneralCompute laneでBlock処理
  ↓
共有ArchiveCompression laneで量子化・予測差分・圧縮
  ↓
journal付きatomic commitでPreload DBへ保存

Flight開始
  ↓
Builderの全球生成を停止
  ↓
Required Tile Setをgeneration付きで発行
  ↓
Hot RAM → Warm RAM → Preload DBの順で検索
  ↓
共有Scheduler上でchunk read・CRC・展開・高度復元
  ↓
main threadは完成BufferのGPU反映だけを担当
  ↓
DB missだけTerrain Block PipelineでPQS補完
  ↓
Final Tile完成後に低優先度でDBへ追加
```

## 3. Preload Terrain Map Builder

### 3.1 対象シーン

Builderは`HighLogic.LoadedSceneIsFlight == false`の時だけ全球生成を許可する。これによりSpace Center、VAB、SPH、Tracking Station、Main Menu後の安全な非Flight状態を一つの境界で扱う。

Flight中はBuilderによる全球走査を停止し、次だけをTerrain Block Pipelineで補完する。

- 現在viewport
- 自機直下
- track前方
- 選択LAND滑走路と進入周辺
- 登録済み滑走路周辺
- 将来NAV route providerが発行するcorridor

新NAVはBLOCKED中のため、現時点ではNAV corridor自体を生成しない。lane、point契約、世代境界は先に用意してある。

### 3.2 モード

- OFF：自動生成なし
- MANUAL：BUILD指示を受けた天体だけ生成
- IDLE ONLY：無操作時だけ生成
- BACKGROUND：非Flightで継続するが低負荷
- AGGRESSIVE IDLE：操作中は後退し、無操作時間に応じて段階加速

既定値はAGGRESSIVE IDLE。

### 3.3 入力と負荷制御

`Input.anyKey`、マウス移動、各マウスボタンを監視する。入力再開時はPQS query rate、1 frame当たりsample数、worker投入量を後退させる。無操作時は急激な全開ではなく、idle ageに基づき約8秒かけてrampする。

負荷制御は既存Performance Runtimeのactive profile、frame time、worker permit、queue状態を尊重する。専用ThreadPoolは作らない。

### 3.4 天体優先度

手動指定を自動判定より優先する。

- PINNED
- HIGH
- NORMAL
- LOW
- DISABLED

自動判定では現在天体、Kerbin、LAND／滑走路pointを持つ天体、最近訪問天体を優先する。PQSを持たない天体、恒星、地表生成に不適切な天体はDISABLEDとなる。

### 3.5 天体内部の生成順

1. Global Overview
2. 現在地点・高優先point
3. 登録済み滑走路
4. LAND／Go-Around用point
5. Far全球
6. Route全球
7. 地域Local
8. LAND局所

実装は「1地域の最高精細化」より「広域の最低限被覆」を先行させる。Global phaseが未完の間に、全域が局所Finalだけで埋まることはない。

### 3.6 進捗継承と更新競合防止

`preload_state.aps`へ次を保存する。

- mode
- 天体優先度と手動override
- quality上限
- body容量上限
- last visited
- Global／Far／Route cursor
- point cursor
- environment hash
- pause状態

保存は一時ファイル、backup、atomic replaceで行う。

滑走路・現在地点のpoint一覧は2秒周期で再構築されるが、正規化・安定sort・署名比較を行い、**内容が変わった時だけ**point scanとBuilder generationを更新する。同一snapshotの反復で生成中Tileをstale取消ししない。

## 4. LOD構成

既存CP2のbody-fixed tileを共通interfaceとして利用する。

- Global：全球Overview、160／80km向け
- Far：広域航法
- Route：通常航法・corridor
- Local：現在地と最近使用地域
- LAND：滑走路・進入・Go-Around局所

解像度は天体半径、tile角度、quality、用途から決定する。全固体天体を一律Ultraへしない。PreviewとFinalを分離し、viewport全体の利用可能性を優先する。

## 5. Preload Terrain Database

### 5.1 形式

CSVは地形本体に使用しない。実装は外部ネイティブ依存のない独自形式を採用する。

```text
TerrainPreloadDatabase/
  manifest.atm
  manifest.atm.bak
  preload_state.aps
  preload_state.aps.bak
  Journal/
  Chunks/<body>/<lod>/<spatial>.atb
```

- Database Format Version：2
- Codec Version：1
- 1 chunk：8×8 Tileの空間group
- Tile順：Morton order
- 起動時：manifest/indexだけを読む
- Blob：要求されたchunkだけworkerで読む

### 5.2 Tile Metadata

各Tile recordへ次を保持する。

- format／codec version
- Body name、body radius
- environment hash
- tile coordinate、LOD、resolution
- 緯度経度境界
- min／max elevation
- quantization offset／scale
- quality、generation state
- generation／last access time
- PQS configuration hash
- GameData hash
- Terrain Generation ID
- uncompressed／compressed size
- payload CRC
- Water／Constant Height／Flat Tile flag

### 5.3 高度圧縮

生float配列は永続保存しない。

1. Tile min/maxからoffsetとscaleを算出
2. 各高度をUInt16へ量子化
3. 左sample基準のRow predictorで差分化
4. Water／Constant／Flatを短縮表現
5. Deflate圧縮
6. 圧縮で増える場合はRaw fallback

現在の配布互換性を優先して標準.NET/MonoのDeflateとRawを実装した。Codec IDとversionはDB formatから分離しており、将来Zstd／LZ4 providerを追加してもindexを再設計しない。

LAND／Protectの最終安全判断は、この量子化地形だけを唯一の根拠にしない。

### 5.4 原子的保存と部分復旧

保存手順：

1. encoded Tile生成
2. Journalへpending marker
3. chunk一時ファイル作成
4. 全record CRCとpayload CRCのround trip検証
5. atomic replace
6. index更新
7. manifest atomic保存
8. pending marker削除

KSPが途中終了した場合、次回起動は索引だけで開始する。pending markerまたはmanifest不整合があれば、非Flight maintenance workerがchunkを走査してindexを再構築する。

recordはlength framingされているため、1 TileのCRC破損で隣接TileやDB全体を捨てない。破損Tileだけindexから外し、必要時にPQS再生成する。

### 5.5 地形変更検知

Tile keyにbody radius、format、body-specific live PQS environment hashを含める。これが変わった天体だけを無効化する。

GameData hashもrecordへ保存・照合・telemetry記録する。ただし無関係MOD変更で全天体を消さないため、選択的無効化の最終判定はbody-specific PQS hashを優先する。

## 6. 非同期・並列読込み

### 6.1 Main Threadに残すもの

- vessel／body／view snapshot
- Required Tile Set作成
- Unity／PQS状態の必要最小取得
- 完成BufferのTexture／RenderTexture反映
- 最終描画

### 6.2 Backgroundへ移したもの

- index／chunk位置解決
- chunk disk read
- spatial read coalescing
- record CRC／payload CRC
- codec展開
- predictor復元
- UInt16高度復元
- metadata／generation検証
- GPU staging／CPU fallback buffer整形
- DB commit、manifest、verify、cleanup

描画処理から`File.ReadAllBytes`、worker待機、`Task.Wait`、`Task.Result`を実行しない。

### 6.3 Read lane

- CRITICAL：現在viewport、自機直下、選択LAND
- HIGH：track前方、現在／次corridor
- NORMAL：PLAN、中距離、traffic関連
- PREFETCH：look-ahead
- BACKGROUND：遠方、検証、整理

同一chunk内の複数Tileは1回のreadへまとめる。Flight read要求がある時、Builder writeは最後のI/O slotを使用できない。

### 6.4 I/O適応

固定のストレージ種類推測だけに依存せず、read latencyとPerformance profileから並列上限を調整する。無制限read、無制限decode queue、巨大main-thread blob展開は行わない。

## 7. RAM／Disk／VRAM階層

- Hot RAM：展開済みHeight Tile
- Warm RAM：圧縮済みencoded Tile
- Cold：Preload DB
- VRAM：現在表示に必要なGPU texture／buffer

優先保持は現在viewport、track前方、LAND、選択滑走路。単純LRUだけでなくpriority、visible、distanceを使う。容量超過時は古い遠方高LODから削除し、Global、現在天体、Kerbin、PINNED、滑走路／LANDを保護する。

## 8. Terrain Block Pipeline

DB missだけを補完する。

- PQSはmain threadで時間・sample数・QPS上限付き
- 33×33等のTileを複数Blockへ分割
- 複数Tileをround-robin
- 最大active Tileとpending Blockをbounded化
- Block処理はGeneralCompute
- 25%単位でProgressive Commit
- 未完成領域は既存High／低LOD／Global／CPU fallbackの順で保持
- 黒背景へ抜かない
- FinalだけDBへ保存し、PreviewはRAM／GPU専用

Tile SourceはHot RAM、Warm RAM、Preload DB、Realtime Generated、Global Fallbackを共通interfaceへ変換するため、同一viewportへ混在できる。

## 9. 世代番号

各要求・read・decode・PQS・commitへ次を保持する。

- Body Generation
- Vessel Generation
- View Generation
- Range Generation
- Plan Generation
- Terrain Generation
- Database Request Generation
- Request Sequence

天体、機体、Range、PLAN、RECENTER、LAND選択、品質、DB rebuild変更後の古い結果は、disk read完了後であっても表示へ反映しない。

## 10. 容量管理

全体設定：

- 512MB
- 1GB
- 2GB
- 5GB
- 10GB
- 20GB
- UNLIMITED

天体別cap：AUTO／256MB／512MB／1GB／2GB／5GB。

削除は古い未使用LAND／Local、遠方高LOD、未使用天体のRoute／Farから行う。Global、active body、Kerbin、PINNEDを保護する。

## 11. 管理UI

非FlightではAERISウィンドウを`PRELOAD TERRAIN MAPS`専用画面として再利用する。FlightではSYSTEMの`PRELOAD MAPS` pageから状態を確認できる。

操作：

- BUILD
- PAUSE
- RESUME
- CANCEL
- VERIFY
- REBUILD（二段階確認）
- DELETE（二段階確認）
- priority変更
- quality上限変更
- body容量変更
- PRELOAD MAP STORAGE変更
- idle ramp設定

## 12. ND更新との分離

Tile生成・readが遅れても毎描画フレームの次を維持する。

- ownship
- track／TRACK UP回転
- runway
- current／next guidance symbol
- selection
- alert
- 既存Tile再投影

高負荷時の劣化順はBuilder停止、background prefetch停止、look-ahead短縮、遠方Final停止、遠方LOD低下、等高線簡略化、遠方label簡略化。現在viewport、ownship、TRACK UP、runway、LAND、alertを維持する。

## 13. Telemetry

Builder：

- `preload_builder_body`
- `preload_builder_lod`
- `preload_builder_tiles_complete`
- `preload_builder_tiles_pending`
- `preload_builder_pqs_ms`
- `preload_builder_worker_utilization`
- `preload_builder_write_mbps`
- `preload_builder_compression_ratio`
- `preload_builder_storage_bytes`

DB read／decode：

- `terrain_db_read_requests`
- `terrain_db_read_latency_ms`
- `terrain_db_read_mbps`
- `terrain_db_read_queue_depth`
- `terrain_db_cache_hit_ratio`
- `terrain_db_coalesced_reads`
- `terrain_db_crc_failures`
- `terrain_db_hash_mismatches`
- `terrain_decompress_queue_delay_ms`
- `terrain_decompress_time_ms`
- `terrain_decompress_mbps`
- `terrain_decompress_worker_active`
- `terrain_decompress_failures`

表示：

- `terrain_first_tile_visible_ms`
- `terrain_viewport_coverage_ratio`
- `terrain_preload_result_age_ms`
- `terrain_stale_results_discarded`
- `terrain_generation_fallback_count`

既存Hotfix 3の`terrain_tile_*`、`terrain_gpu_*`も維持する。

## 14. 安全境界

- AP／BANK sourceは変更しない
- LANDに操縦権限を追加しない
- 旧NAVを戻さない
- 新NAVを実装しない
- TerrainからFlightCtrlState／MainThrottleへ書かない
- SchedulerのSafety／LAND reserved laneをTerrainで使用しない
- ND専用ThreadPoolを作らない
- Builder writeをFlight critical readと同優先度にしない
- DB完成までNDを非表示にしない

## 15. 現時点の未試験

静的回帰は通過しているが、次は未試験である。

- Mono/xbuildによるネイティブC#コンパイル
- KSP 1.12.5の全非Flight scene lifecycle
- 実PQS Builder throughputとUI入力遅延
- Linux／Windowsのatomic replace差
- NVMe／SATA SSD／HDDのI/O適応
- 実GPU部分合成
- 破損chunkの実KSP復旧
- 地形MOD導入前後の選択的無効化
- 数時間の容量・queue・worker・VRAM boundedness

これらは`ND_CP2_PRELOAD_TERRAIN_TEST_CARD_v0.18.0.0_ja.md`で判定する。CP2合格前にCP3へ進まない。
