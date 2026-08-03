# AERIS v0.18.0.0 DEV CP2 Preload Terrain／Status Toolbar 引き継ぎ

> この文書はPreload統合時点の履歴である。現在の再開点は
> `HANDOVER_AERIS13_v0.18.0.0_CP2_FIELD_RENDER_HOTFIX1_ja.md`を正とする。

## 1. 現在地

Gate 0～5のソース実装と静的回帰が完了し、CP2実KSP試験待ち。

改良型NDの地形主供給源は次へ変更済み。

```text
Preload Terrain Database
  ↓ miss only
Terrain Block Pipeline
```

Flight中のPQS全面生成を主系統へ戻してはならない。

## 2. 主要ソース

### 共通Tile／GPU

- `Terrain/AERISTerrainTileContracts.cs`
- `Terrain/AERISTerrainTileCache.cs`
- `Terrain/AERISTerrainTileSystem.cs`
- `Terrain/AERISTerrainGpuTileRasterizer.cs`
- `Terrain/AERISTerrainGpuTileRenderer.cs`
- `Terrain/AERISTerrainPerformance.cs`
- `Terrain/AERISTerrainAwareness.cs`

### Preload中核

- `Terrain/AERISTerrainPreloadContracts.cs`
- `Terrain/AERISTerrainPreloadCodec.cs`
- `Terrain/AERISTerrainPreloadDatabase.cs`
- `Terrain/AERISTerrainPreloadBuilder.cs`
- `Terrain/AERISTerrainBlockPipeline.cs`

### Runtime／UI

- `Performance/AERISPerformanceRuntime.cs`
- `Performance/AERISWorkerScheduler.cs`
- `Core/AERISBootstrap.cs`
- `Settings/AERISSettings.cs`
- `UI/AERISWindow.cs`
- `UI/ToolbarBridge.cs`
- `UI/AERISNavigationDisplay.cs`

## 3. Runtime data

- Preload DB：`GameData/AERISFlightControl/PluginData/TerrainPreloadDatabase`
- Manifest：`manifest.atm`
- Builder state：`preload_state.aps`
- Spatial chunk：`Chunks/**/*.atb`
- Journal：`Journal/`
- ND profile：`GameData/AERISFlightControl/Config/NavigationDisplayProfiles.cfg`
- Settings：`GameData/AERISFlightControl/Config/AERISSettings.cfg`

source packageへこれらruntime生成物を混入させない。

## 4. 絶対条件

- AP／BANKを変更しない
- 旧NAVを復活させない
- CP2合格前に新NAVへ着手しない
- LANDへ操縦権限を与えない
- ND／Terrain専用ThreadPoolを作らない
- PQSをworkerから直接呼ばない
- Main ThreadでDisk read／decode／worker待機を行わない
- Builder writeにFlight critical readと同じ優先度を与えない
- Previewを永続DBへ大量保存しない
- partial coverageを黒背景で上書きしない
- DB破損で無関係天体を全削除しない
- new Tile待ちでownship／runway／TRACK UPを止めない
- sceneごとに別Toolbar ownerやMain Menu overlayを作らない
- 非Flight status画面からPreload状態を変更しない
- Launcher destroyed後の旧visible-state同期をそのまま信頼しない


## 4A. Toolbar／Status契約

- `AERISBootstrap`はMainMenu startupかつpersistentで、Toolbar ownerもそのGameObjectへ1個だけ追加する
- `ToolbarControl.RegisterMod`と`AddToAllToolbars`は各1回
- 対象sceneは`ApplicationLauncher.AppScenes.ALWAYS | TRACKSTATION`
- Launcher ready／destroyedとAERIS scene boundaryでvisible-state cacheをinvalidateする
- ToolbarのON callbackはsceneを見てFlight窓または非Flight status窓の一方だけを開く
- ToolbarのOFF callbackも同じsceneの窓だけを閉じる
- `HighLogic.LoadedScene`の全変更を追跡し、非Flight同士を含むscene遷移時は両方の窓を閉じて次sceneへ自動継承しない
- 非Flight statusは`AERISTerrainPreloadStatusSnapshot`の読取りだけを行い、mutation APIを呼ばない
- 38×38 stock iconと24×24 Blizzy iconのstable Texture名を維持する

## 5. 共有Scheduler lane

- `GeneralCompute`：chunk read、decode、validation、Block processing、hash
- `ArchiveCompression`：codec encode、chunk commit、manifest、state、maintenance
- `Safety`／LAND reserved lane：Terrain使用禁止

read/write I/Oはbounded。Flight readがある場合、writeは最後のslotを取れない。

## 6. DB互換性

- Manifest magic：`AERIS_PRELOAD_TERRAIN_MANIFEST_V2`
- Chunk magic：`AERIS_PRELOAD_TERRAIN_CHUNK_V2`
- State magic：`AERIS_PRELOAD_TERRAIN_STATE_V1`
- Database format：2
- Codec version：1

format変更時はversionを上げる。既存versionを同じ番号のまま意味変更しない。

## 7. 既知の重点リスク

1. Mono compilerで新規C#構文／APIが受理されるか
2. Main Menuで`FlightGlobals.Bodies`／PQSが利用可能になる時期
3. VAB／SPH操作中のPQS budget
4. File.ReplaceのLinux／Windows差
5. 大chunkのwrite amplification
6. HDDでのread coalescing効果
7. body-specific PQS hashのMOD網羅性
8. dateline／pole／極小天体
9. corruption recoveryとjournal marker
10. capacity prune時の保護優先度
11. long-run warm cacheとVRAM解放
12. scene change直後のstale callback
13. user setting／state fileの競合
14. 2秒point refreshがgenerationを進め続けないこと
15. ToolbarControlのMain Menu／Tracking Station対応とscene再バインド
16. launcher破棄／再生成後のicon重複・状態逆転

## 8. 実KSPログで見る列

- `preload_builder_*`
- `terrain_db_*`
- `terrain_decompress_*`
- `terrain_first_tile_visible_ms`
- `terrain_viewport_coverage_ratio`
- `terrain_preload_result_age_ms`
- `terrain_stale_results_discarded`
- `terrain_generation_fallback_count`
- 既存`terrain_tile_*`
- 既存`terrain_gpu_*`

## 9. CP2合格基準

- non-Flight Builderが入力を妨げず進む
- progressが再起動後に継続
- generated TileがFlightで非同期readされる
- DB hit範囲のPQS依存が減る
- miss範囲をBlock単位で表示しFinalを保存
- Range／PLAN変更後に旧結果が戻らない
- queue／RAM／VRAM／Diskがbounded
- corruption／MOD変更を局所復旧
- ND symbolがTerrain待ちで止まらない
- AP／BANK／LAND／NAV境界を維持

## 10. 次工程

CP2合格後のみCP3を開始する。

- Physical runway directionとprocedure registry接続
- DIRECT／OFFSET／DOGLEG／STEEP
- 2.5～6.0°の制約付きGlide Profile
- terrain／static obstacle corridor
- missed approach同時成立
- LAND plan／profile表示

依然として計画・表示のみ。操縦権限追加は後段の別Gate。
