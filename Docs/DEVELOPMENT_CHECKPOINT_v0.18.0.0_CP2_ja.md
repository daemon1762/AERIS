# AERIS v0.18.0.0 DEV CP2 開発チェックポイント

## チェックポイント名

**CP2 Field Render Consistency Hotfix 1**

正式版・RCではない。CP2実機試験を通過するまでGate 6以降へ進まない。

## 継承元

- Gate 0：Physical Runway Canonical Federation
- CP1：ND Core／Runway／PLAN
- CP2 Terrain Tile／GPU Terrain
- Compile Hotfix 1
- Render Hotfix 2
- Terrain Supply Hotfix 3

今回、Hotfix 3の最新viewport優先Pipelineを残したまま、非Flight事前生成と永続主供給DBを一括統合した。

その後の実KSP証拠で確認したTile lifecycle／GPU合成／coverage不整合を、本チェックポイントで修正した。修正前runtimeはFAILであり、本版の実KSP再試験は未実施。

## 今回の追加

### Preload Terrain Map Builder

- 全非Flight scene境界
- OFF／MANUAL／IDLE ONLY／BACKGROUND／AGGRESSIVE IDLE
- input-driven gradual backoff／ramp
- 天体別PINNED／HIGH／NORMAL／LOW／DISABLED
- Global-first LOD plan
- current point／runway／LAND point優先
- state／cursor／priority／quality／pauseの永続化
- Flight移行で全球生成停止

### Preload Terrain Database

- format version 2、codec version 1
- binary manifestと外部spatial chunk
- 8×8 Tile chunk、Morton order
- UInt16量子化、Row predictor
- Water／Constant／Flat短縮
- DeflateとRaw fallback
- record CRC＋payload CRC
- journal＋atomic replace＋manifest backup
- index-only startup
- chunkからの非Flight index復旧
- Tile単位またはchunk単位の局所隔離

### 非同期読込み

- Hot RAM／Warm RAM／Cold Disk／VRAM
- CRITICAL／HIGH／NORMAL／PREFETCH／BACKGROUND
- spatial read coalescing
- shared GeneralComputeでread／decode／validation
- shared ArchiveCompressionでwrite／compression
- Flight read優先I/O arbitration
- measured latencyに基づくread並列度調整
- generation／sequenceによるstale拒否

### Terrain Block Pipeline

- DB missだけをPQS補完
- time／QPS／sample bounded main-thread取得
- Tile内Block分割
- 複数Tile round-robin
- GeneralCompute worker処理
- 25% Progressive Commit
- PreviewはRAM／GPUのみ
- FinalだけDB commit
- CPU fallback上へ部分HD合成

### 管理・診断

- PRELOAD TERRAIN MAPS UI
- PRELOAD MAP STORAGE
- body priority／quality／cap
- BUILD／PAUSE／RESUME／CANCEL／VERIFY／REBUILD／DELETE
- Builder、DB read、decompression、display telemetry

## FMJ方式Toolbar小改良

- 永続Bootstrap配下の`ToolbarBridge`を単一ownerとして維持
- ToolbarControl登録は`ALWAYS | TRACKSTATION`へ拡張
- Main Menu／Space Center／VAB／SPH／Tracking Station／Flightで同一AERISアイコンを使用
- `onGUIApplicationLauncherReady`／`Destroyed`でscene再生成後の表示同期を無効化し、次Updateで再同期
- scene境界でもtoolbar bindingを明示的にinvalidate
- duplicate owner、複数ToolbarControl、Main Menu専用overlayを禁止
- 非Flightは独立した`PreloadStatusVisible`だけを切り替え、Flightの`Visible`と混同しない
- 非Flight窓は進捗・状態の読み取り専用で、Builder／DB変更APIを呼ばない
- Flightでは従来のAERIS Flight Control窓を維持

## 監査中に修正した競合

滑走路・現在地点のPreload point一覧は2秒周期で再取得される。初期統合では同一内容でも毎回`BodyPlan.Generation`と`PointCursor`を更新していたため、低負荷sceneで数秒以上かかる生成中Tileがstale扱いとなる可能性があった。

修正：

- pointをsanitise
- priority、body、緯度、経度、LOD、reasonで安定sort
- invariant round-trip数値で署名生成
- 同一署名では何もしない
- 内容変更時だけcursor、scan count、target estimate、generationを更新

専用回帰を追加し、同一snapshot反復が生成中Tileを取消さないことを固定した。

## 実装上の選択

- SQLiteは採用しない。KSP Mono／Linux／Windowsへ追加ネイティブ依存を持ち込まない。
- 初期codecはDeflate＋Raw。codec abstractionは将来の高速provider追加を許す。
- GameData hashは保存・検証・telemetry化するが、無関係MOD変更による全消去を避けるため、body-specific PQS environment hashを選択的無効化の主判定にする。
- 新NAVはBLOCKEDのため、NAV corridor laneは契約だけ用意しactive routeは発行しない。

## 安全境界

- `AERISBankDirector.cs`の基準SHAを維持
- AP／BANK control law変更なし
- LAND FoundationはFlightCtrlStateを持たない
- Terrain／Builderから操舵・throttleへ書き込まない
- legacy NAV class不在
- new NAV capability未公開
- Safety／LAND reserved worker lane不使用
- ND専用ThreadPoolなし

## 静的受入

最終数値は`ACCEPTANCE_v0.18.0.0_CP2_FIELD_RENDER_CONSISTENCY_HOTFIX1.txt`を正とする。

受入には次を含む。

- Gate 0全回帰
- CP1全回帰
- CP2 Terrain／GPU／auxiliary
- Compile Hotfix 1
- Render Hotfix 2
- Terrain Supply Hotfix 3
- Preload統合専用回帰
- Field Render Consistency専用回帰
- manifest一致

## 未試験

- FMJ方式Toolbarのnative Mono/xbuild compile
- KSP 1.12.5 ToolbarControl／scene runtime
- Unity GPU rendering
- real PQS cost
- non-Flight scene input latency
- actual disk latency adaptation
- crash／corruption restoration
- terrain MOD selective invalidation
- long-run RAM／VRAM／Disk boundedness

これらをPASSと表現しない。

## 次の再開位置

ユーザー実機試験で本HotfixのCP2が合格した後だけCP3へ進む。

CP3：

- Gate 6 Approach Procedure Registry接続
- Gate 7 variable Glide Profile
- Gate 8 3D obstacle／missed-approach corridor

CP3でもLANDへ飛行制御権限はまだ与えない。
