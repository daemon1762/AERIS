# AERIS Flight Control v0.18.0.0 DEV CP3.75 — Pure ND Rebase Candidate 1

CP3.75 Candidate 1 abandons the rejected CP3.5 ND presentation line and restores the exact CP3 Gate 5 Candidate 14 ND terrain/projection authority on top of the latest AERIS20 non-ND baseline. Terrain presentation, coastline/contours, runway projection, ownship/map geography, fixed 5/10/20/40/80/160 km range behavior, Current-Body Resident Cache dependencies and solid-surface-only preload are restored from the Candidate 14 Golden Authority.

This is deliberately a **pure recovery candidate, not a performance optimization**. The later verified FDR/CVR bounded archive-retention feature and other protected non-ND AERIS20 runtime code remain preserved. Runtime acceptance must first prove CP3-late visual quality, geographic authority, runway visibility and stability before any performance work resumes.

# AERIS Flight Control v0.18.0.0 DEV CP3.5 Gate 4 — CP3 Golden Cartographic Quality Candidate 2

Gate 4 Candidate 1 runtime is rejected. Candidate 2 makes the user-supplied late-CP3 screenshots the **Golden Visual Reference** and restores the proven CP3 cartographic reconstruction path as the quality floor.

- LOW keeps REAL33 terrain authority but uses CP3 VIRTUAL LOCAL97 at <=20 km, VIRTUAL ROUTE65 at 20..80 km, and CP3 FAR/Hi-DPI at long range.
- MIDDLE keeps REAL33 and uses CP3 LOCAL97/ROUTE65 reconstruction plus bounded supersampling; no quality-only PQS pass is added.
- HIGH keeps a full-map CP3-quality fallback and selectively upgrades complete bounded REAL65 tiles to VIRTUAL129.
- Categorical land/sea reconstruction, exact fill-diagonal coastline, sub-cell ocean crossing and contour generation from reconstructed geometry are retained from the late-CP3 path.
- Candidate 1 HIGH refinement no longer advances global `terrainGeneration`; new detail uses `gpuContentRevision`, preventing transient REAL65 completions from continuously invalidating Exact FRONT.
- Exact FRONT authority, ownship fixed-anchor, ownship-relative prediction and wrong-range FRONT rejection from Hotfix 1 remain frozen.
- The three Golden Visual Reference screenshots are embedded under `Evidence/CP3_GOLDEN_VISUAL_REFERENCE/`.

Runtime acceptance is mandatory: LOW itself must be no worse than the embedded CP3 Golden references, and stable FAR-ready viewports must not fall to blue-only/BUILDING.

# AERIS Flight Control v0.18.0.0 DEV CP3.5 Gate 3 — Candidate 2 Presentation Authority Hotfix 1

## Exact FRONT Authority / Temporal Shadow Recovery

AERIS58 runtime evidence showed that Candidate 2 could report a valid temporal reprojection (`confidence=1.000`, sub-pixel error) while the actual ND presentation surface was ocean-blue/empty. Hotfix 1 therefore restores the committed Exact FRONT RenderTexture as the hard presentation authority.

- Temporal reprojection remains available only as a **shadow-quality probe** for refresh/error telemetry; it cannot own the visible ND in this hotfix.
- The Exact FRONT is presented directly with an **exact overscan crop** about horizontal center 0.5 and the committed ND vertical anchor, so a 1.25x key-frame surface is not incorrectly scaled into the visible range.
- `NaN`/`Infinity` temporal coordinates are rejected explicitly.
- Temporal material setup is completed before the presentation RenderTexture may be cleared, preventing a failed `SetPass` from blanking a previously valid surface.
- World-locked runway/airfield/route overlays continue to consume the renderer's committed presented projection, preserving map lock.
- CP3 Frozen visual-detail policy, multicore exact projection, accessibility palette fixes, compact responsive UI, and terrain-quality LAND removal remain unchanged.

Runtime acceptance priority: **ND must always become visible once an Exact FRONT exists.** Performance/temporal smoothness is secondary for this hotfix and will be resumed only after exact visibility is confirmed.

# AERIS Flight Control v0.18.0.0 DEV CP3.5 Gate 3 — CP3 Frozen Visual Path Recovery / Bounded Exact Refinement Candidate 2

## v0.18.0.0 DEV CP3.5 Gate 3 — CP3 Frozen Visual Path Recovery / Bounded Exact Refinement Candidate 2

Candidate 1 is rejected for runtime use because live 160 km refinement could promote visible Route/Local working sets into repeated Mesh build/upload/evict cycles. Candidate 2 restores the exact CP3 frozen visual-detail policy as the Golden Visual Reference while retaining CP3.5 multicore projection, Exact Key Frames, overscan temporal reprojection, accessibility palette fixes, unified world-surface phase 1 and compact responsive UI.

- FAR DIRECT remains 33x33; CP3 VIRTUAL ROUTE remains 65x65 and VIRTUAL LOCAL remains 97x97.
- Flight viewport exact Route/Local refinement is **existing-only**. Missing detail is never generated for presentation.
- The CP3 near-LOD thresholds are restored: Local <=10 km, Route <=40 km, FAR above 40 km. The rejected 160 km Route promotion is removed.
- HIGH returns to the known CP3/Gate2 bounded resource envelope.
- Terrain-quality LAND remains fully removed; AUTOPILOT LAND and LAND guidance remain independent.
- Accessibility palette fixes and palette-generation invalidation are retained.
- Future high-resolution work must use a sparse refinement overlay that cannot evict/replace the FAR base working set.

## Historical Gate 2 Candidate 1 — Parallel Projection / Compact Autopilot UI

Candidate 1 first moved the dominant exact ND BACK preparation work off the Unity main thread. Its multicore projection architecture is inherited by Candidate 2, but Candidate 2 adds overscan and temporal GPU presentation and removes the synchronous main-thread projection fallback.

# AERIS Flight Control v0.18.0.0 DEV CP3.5 Gate 1 — Back Render Profiler / Symmetric Top UI Candidate 2

Candidate 2 is a **diagnostic bridge, not the final ND presentation architecture**. Candidate 1 proved that suppressing forced recovery works, but the 160 km 0.50 s cadence only reduced the terrain presentation to about 2 Hz while the expensive BACK render itself remained. Candidate 2 therefore restores every ND range to the scheduled **0.20 s BACK cadence**, keeps forced-recovery full renders suppressed, and profiles the remaining BACK cost so Gate 2 can remove the real main-thread bottleneck instead of hiding it behind a slow display.

- Every fourth BACK render receives low-rate detailed timing. The normal BACK total is still sampled every render. `[CP3.5_GATE1_BACK_PROFILE]` reports setup/clear, CPU geographic projection, `mesh.vertices` upload, `RecalculateBounds`, colour CPU work, colour upload, `DrawMeshNow`/material submission, finalize, residual/other time, tile/vertex/draw-call counts, and the active cadence. No per-tile log spam is added.
- Candidate 1's forced-recovery suppression and exact latched-FRONT projection authority remain. `GUI.matrix` terrain warping and CPU terrain presentation remain prohibited.
- The AERIS main-window top chrome now follows the supplied screenshot reference: MASTER fills the available row; `FLIGHT CONTROL / PROTECT / AUTOPILOT` is an equal three-way row; `SYSTEM / EXTEND ADDONS` is an equal two-way row. Rows are computed from the available window width with equal left/right outer margins and equal internal gaps, so resizing cannot leave an ugly one-sided blank area.
- The same symmetric row rule is used for SYSTEM's top selectors. Candidate 13's deliberate removal of SYSTEM DIAGNOSTICS is preserved; `PRELOAD MAPS` fills its remaining row instead of recreating a dead button.
- Window resizing remains continuous/responsive. Text, labels, and buttons are explicitly non-wrapping and clipped rather than allowed to create content-driven coordinate jumps.

Runtime acceptance focus: 160 km at approximately 2100 m/s, `forced_recovery=0`, `back_cadence_s=0.20`, several `[CP3.5_GATE1_BACK_PROFILE]` samples, ND ON/OFF frame-time comparison, and min/mid/max window resize symmetry/no-wrap inspection.

# AERIS Flight Control v0.18.0.0 DEV CP3.5 Gate 1 — Presentation Cadence / Responsive UI Candidate 1

CP3.5 Gate 1 removes the Gate 4B forced full-render recovery loop from the ND terrain presentation path. FRONT compatibility loss now requests a BACK refresh but cannot bypass the explicit presentation cadence. The last complete GPU FRONT is shown without `GUI.matrix` warp and publishes its exact committed projection to terrain/runway/traffic/ownship world-fixed overlays until the next BACK atomically swaps. Cadence is range-aware: 5–20 km 0.20 s, 40 km 0.25 s, 80 km 0.33 s, 160 km 0.50 s. `forced_recovery` must remain zero; `forced_recovery_suppressed` records prevented bypass frames. CPU terrain presentation remains prohibited.

The main AERIS window also supersedes Candidate 9 fixed button dimensions by restoring window-responsive control geometry. Width follows window width continuously and control height follows window height within bounded scaling. Geometry is determined only by window dimensions: text/content may not change control size or flow. Automatic word wrapping is disabled for AERIS window labels/buttons and ND shared text/button styles; long text is clipped rather than silently moving later controls. Existing resize pointer anchoring and screen clamps remain unchanged.

## v0.18.0.0 DEV CP3 Gate 5 Candidate 14 — Solid-Surface Preload Exclusion Hotfix 1

Automatic terrain preload is now strictly limited to celestial bodies that expose a body-local PQS terrain controller. Stars, gas giants, and any other bodies without a solid terrain surface are fail-closed and excluded from automatic preload, PRELOAD body presentation, manual BUILD/RESUME/REBUILD generation paths, and current-body terrain support. This is capability-based rather than name-based, so mod-added surface-less bodies are excluded automatically. Existing stored data is not destructively deleted.

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 5 — Integrated Acceptance Candidate 13 — Final UI / Preload Policy Hotfix 1

Candidate 13 freezes the user-facing preload policy for final runtime acceptance. Automatic preload now exposes only ON/OFF and otherwise uses the accepted AERIS default policy (AggressiveIdle, Balanced producer profile, 5 s idle threshold). Persistent preload storage is unlimited, per-body Priority/Quality/BodyCap tuning is removed, and each body exposes only BUILD, PAUSE/RESUME, DELETE and REBUILD. The preload page displays only body identity and completion percentage; SYSTEM diagnostic telemetry and PROTECT live debug telemetry are removed from the UI. PROTECT Parking Hold and automatic reverse-thrust defaults are ON with one-time legacy settings migration. Making History runways are presented under VANILLA RUNWAYS while retaining DLC/UserCalibrated authority internally, and remain hidden when Making History is absent. Dessert RWY 36/18 and the 41-physical-runway / 82-direction baseline are unchanged.

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 5 — Integrated Acceptance Candidate 12 — DLC Dessert Field-Verified Default Baseline — Compile Hotfix 2

Compile Hotfix 2 fixes the remaining build-entrypoint-only regression in the inherited Candidate 11 selftest. `build_ubuntu.sh` regenerates `AERISBuildVersion.generated.cs` before Gate 5 acceptance; the regenerated file keeps normalized Display lineage but previously dropped the decorative Candidate 11 comment that the test treated as authority. Candidate 11 lineage validation now checks the stable normalized Display token, and the generated source template also retains the Candidate 11 historical marker for traceability. The Hotfix 2 regression explicitly guards source-vs-generated identity determinism. No operational C# behavior, runway geometry, ND/LAND logic, Terrain, AP/FBW/PROTECT, or the 41 physical runway / 82 reciprocal direction baseline is changed.

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 5 — Integrated Acceptance Candidate 12 — DLC Dessert Field-Verified Default Baseline

Candidate 12 promotes the user field-surveyed Making History Dessert Airfield A/B absolute geodetic endpoints into the shipped default authority. Dessert is now defined as RWY 36/18 using the exact 2026-08-02 manual marks, and those same marks are appended to `03_Field_Verified_Runway_Calibrations.cfg`. The configured direction explicitly carries `UserCalibrated` certification basis, so the existing non-Stock automatic-certification prohibition remains intact: this is manual field authority shipped as a default, not inferred DLC geometry. DLC absence still hides the airport through Candidate 10 provider/expansion visibility rules.

Candidate 11 fixes the Candidate 10 field-registration dead end: an installed DLC runway placeholder with no runtime geometry can now be opened in SYSTEM > AIRFIELDS and directly MARK A / MARK B / CLEAR using the normal body-fixed geodetic manual calibration authority. The placeholder also displays the captured A/B LAT/LON/ALT values so they can be field-verified and promoted into the shipped DLC default baseline without guessing geometry.

Candidate 10 provider-aware visibility remains inherited: absent external airport mods stay hidden and non-selectable, while installed Making History may expose Dessert Airfield as a PENDING placeholder before runtime geometry is available. Candidate 9 fixed-geometry UI and 4 Hz display-telemetry caching remain inherited.


- AERIS-owned buttons now use fixed geometry and non-wrapping/clipped labels so text, status length, or window-width thresholds cannot silently move or resize controls.
- The only variable-size button family retained by requirement is the AIRFIELD airport/runway selection row; AIRFIELD action/category buttons remain fixed.
- Main/SYSTEM tabs, MASTER, Virtual Attitude, Preload selectors, terrain selectors, LAND direction selectors, NAV/flight-plan actions, and all other AERISWindow button-style controls use explicit width + height. ND controls already use explicit Rect geometry and now explicitly disable button word-wrap.
- PreloadStatus UI telemetry is cached at 4 Hz. User operations invalidate the cache immediately, while terrain generation/preload scheduling remain fully live. This removes full TerrainPreloadDatabase tile-index aggregation from every IMGUI Layout/Repaint event.
- SYSTEM terrain/resident/Map-DRAM/corridor diagnostics are likewise display-rate cached at 4 Hz. ND's existing 2 Hz terrain telemetry and 1 Hz GC sampling remain unchanged.
- Candidate 8 phantom-runway/performance repair and Candidate 7/6 safety/authority baselines remain inherited.

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 5 — Integrated Acceptance Candidate 8 — ND Phantom Runway / Performance Hotfix 1

- Candidate 8 fixes the ND phantom-runway selection path by using the exact presented GPU FRONT projection for runway hit-testing and by rejecting truly off-screen runway candidates.
- ND visual quality is unchanged: fixed ranges, terrain LOD/resolution, coastline/contour rendering, GPU-only FAR presentation, and selected-runway edge-pointer behavior are retained.
- Performance work is quality-neutral: one shared runway projection per repaint, conservative off-screen runway pre-culling, 2 Hz terrain telemetry snapshots, 1 Hz GC sampling, allocation-free timing probes, reusable Map DRAM terrain-index wrappers, cached Map DRAM size estimates, routine index-log coalescing, and runtime PQS preload load-shedding under frame/ND pressure.
- Candidate 7 expansion detection, Candidate 6 field-verified runway defaults, manual authority policy, and all CP3 safety boundaries remain inherited.

- Candidate 7 separates installed DLC state from runtime runway exposure: Making History and Breaking Ground are detected independently, while Dessert Airfield availability remains a separate runtime/save status.
- Expansion directory checks run on a ThreadPool worker; AIRFIELDS/ND do not add a synchronous SSD read.
- Candidate 6 field-verified runway defaults and the Base-Game Stock-only automatic certification authority policy remain unchanged.

- Candidate 6 ships the completed 2026-08-02 field-verified non-stock runway A/B set as the default manual-authority baseline (40 physical runways / 80 reciprocal directions).
- Shipped baseline file: `GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg`.
- Fresh installs seed PluginData/UserRunwayCalibrations.cfg from the shipped baseline only when no per-install calibration exists; existing user calibration files are never overwritten.
- The baseline remains USER CALIBRATED authority, not automatic certification. Base-game Stock auto authority policy from Candidate 5 is unchanged.

- Candidate 5 authority policy: automatic runway certification is operational authority only for base-game Stock runways. DLC / Kerbal Konstructs / Stock Launchsites Expansion / UserCfg require USER CALIBRATED two-point A/B authority.
- Existing non-stock automatic certification cache entries are purged/quarantined; automatic positive/negative survey results cannot become operational authority.
- Native Spawn Warp keeps the MOD live LaunchPadTransform latitude/longitude/heading but converts provider ASL to terrain-relative AGL before KSP Set Position physics easing, preventing terrain elevation from being added twice.

- Candidate 3のTerrain Generation Bridgeを凍結したまま、Sandbox限定の滑走路登録巡回補助を追加。
- SYSTEM > AIRFIELDSの物理滑走路ごとに `WARP TO MOD NATIVE SPAWN` を1個だけ表示する。
- ワープ先の緯度・経度・方位・ASLはKerbal Konstructs / Stock Launchsites Expansionが提供するlive LaunchPadTransformそのもの。AERISはRWY09/27別、進入方向別、threshold offset、独自スポーン位置を生成しない。
- Safety Hotfix 2では、unpacked vesselへの直接SetPosition/SetRotation/SetWorldVelocityを廃止し、KSP純正`FlightGlobals.SetVesselPosition(..., easeToSurface=true)`へ委譲する。接地時はKSP標準Physics Easing（gravity multiplier 0.05）で緩降下し、12秒間の再ワープを禁止する。
- Career / Scienceではボタン自体を表示しない。滑走路選択・認証・A/B補正・LAND/AP状態は変更しない。
- 破棄された旧Candidate 4 `Sandbox Runway Warp Utility` の60m inset / 350m staging方式は含まない。


- Candidate 2実機試験で残った通常飛行中のプチブラックスクリーンを修正。
- 原因はTerrainGeneration切替フレームで、完成済みGPU FRONTが存在していてもpresentation latchを拒否していたこと。
- Candidate 3では同一天体・同半径・8秒以内の完成FRONTをgeneration rollover中だけ表示bridgeとして許可する。
- bridgeは表示専用であり、LAND/安全判定authorityには使用しない。
- CPU terrain draw=0、GUI.matrix temporal warp禁止、startup airport/runway NONE、Gate 4C virtual detail契約は維持。


- Candidate 1実機試験で確認した短時間のTerrain黒抜けと高レンジ滑走路I字マーカーの浮遊を修正する最終受入Hotfix。
- 最後に完成したGPU FRONTを無変形でラッチし、そのFRONTの確定projectionをTerrain・滑走路・交通・自機シンボル共通authorityとして使用する。
- 40km以上では滑走路端threshold tickを描画しない。CPU terrain presentationは引き続き0。
- 実機受入対象：body transition、scene transition、40 km altitude hysteresis、ND AUTO/OFF、Terrain AUTO/OFF、5/10/20/40/80/160 km、TRACK UP/NORTH UP、LAND ARM/DISARM、長時間RAM/VRAM/SSD telemetry、CP2.5回帰。
- `Tools/analyze_v01800_cp3_gate5_runtime.py <AERISFlightControl.zip|AERISFlightControl.log|directory>` で最終実機証拠を自動判定できる。
- CPU terrain presentationは引き続き禁止。`cpu_terrain_draw=0`、`synchronousSSD=0`、`ready_build_violation=0`が必須。

詳細：`Docs/CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE1_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 4B — ATTR Presentation Recovery Hotfix 1

- Gate 4B実機FAILで確認された`ready==required`かつ`back_foundation=1.000`なのに`TERRAIN GPU BUILDING`が継続する状態機械欠陥を修正。
- `DIRECT不可 + temporal history不可 + FAR foundation完成`では、ViewGeneration/content revision待ちをせず同一RepaintでGPU BACKを強制再構築し、完成時にFRONTへatomic swapする。
- FAR history surfaceを可視viewportより35%広いoverscan範囲で取得・GPU化し、小さな移動／TRACK UP旋回で履歴が即失効しないようにした。
- presentation refreshをtile-set generationだけに依存させず、range、heading、中心移動、FRONT ageから独立判定する。
- `ready foundation + no presented FRONT`が1秒以上継続した場合は`[CP3_GATE4B_READY_BUILDING_VIOLATION]`をERROR記録する。正常受入では0件が必須。
- CPU terrain drawing、CPU safety fallback、UNKNOWN_TERRAINは復活させない。`cpu_terrain_draw=0`を維持。
- 大規模zoom-outなど、旧overscan surfaceに存在しない地形まで創作することはしない。新FAR foundation未完成時の初回BUILDINGはfail-closedとして許容。

詳細：`Docs/CP3_GATE4B_ATTR_PRESENTATION_RECOVERY_HOTFIX1_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 4B — AERIS Terrain Temporal Reconstruction (ATTR)

- Gate 4AのGPU-only FAR presentationを維持し、完成済みGPU FRONTを旧/現在の測地投影から現在viewportへtemporal reprojection。
- body/radius/terrain generation、range、heading、中心移動、球面歪み、viewport coverage、confidenceを検証し、条件外のhistoryはfail-closedで拒否。
- BACK全面再構築をview generationまたはGPU content revision変更時へ限定し、同一状態の毎Repaint swapを停止。
- LOW=`FAR DIRECT`、MEDIUM=`VIRTUAL ROUTE`、HIGH/LAND=`VIRTUAL LOCAL`として、通常巡航のRoute/Local常設payloadを不要化する表示基盤を確立。
- VIRTUAL品質はFARのGPU空間補間＋temporal reprojectionであり、LAND安全判定のauthorityには使用しない。
- CPU terrain drawing／CPU safety fallback／UNKNOWN_TERRAINは引き続き禁止。
- 大きなzoom-outで旧FRONTが現在viewport全域を覆えない場合は地形を創作せず、`TERRAIN GPU BUILDING`で新FAR FRONTを待つ。

詳細：`Docs/CP3_GATE4B_AERIS_TERRAIN_TEMPORAL_RECONSTRUCTION_ATTR_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 4A — Render-Ready Height Field & GPU-Only FAR Presentation — Compile Hotfix 2

- CPU所有のimmutable Render-Ready Height Fieldを追加し、RAM RESIDENTからRENDER READY／GPU READYへの状態昇格を実配線。
- ND地形の最終表示authorityをGPU FRONT RenderTextureへ一本化。
- GPU BACKは非表示で構築し、viewport-authoritative FAR coverageが100%完成した場合だけFRONTとatomic swap。
- 新しいBACKが未完成の間は互換性のある完成済みGPU FRONTを維持し、CPU terrain drawing／CPU safety fallback／UNKNOWN_TERRAIN塗り潰しは行わない。
- 初回の完成GPU FRONTがない間は`TERRAIN GPU BUILDING`を表示し、部分GPU bufferを画面へ露出しない。
- ROUTE／LOCAL未完成領域は完成済みGPU FAR baseを維持し、CPU/GPU混在境界を作らない。
- ND OFF、高度ゲートOFF、viewport停止、scene transition時にGPU resourceを解放し、Render-Ready CPU payloadは再upload用に維持。
- 通常GPU lifecycle release後のlossless再uploadを保証するため、GPU entryが存在するRender-Ready authorityはLRU prune対象から除外。

詳細：`Docs/CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 1 — Current Body Resident Cache Contracts Compile Hotfix 1

- `AERISTerrainPreloadBuilder`で欠落していた`ApplyStandardSchedulerState(bool)`を復元。
- 非Flight STANDARD適用、Flight解除、Dispose解除の3呼出を既存Scheduler APIへ再接続。
- runtime未生成時はfail-safe、状態はScheduler更新成功後にのみcommit。
- CP3 Gate 1のResident Cache契約、`payloadRoute=DISCONNECTED`、FULL BOOST不在を維持。
- 専用compile-regressionをGate 1 runner先頭へ追加。

詳細：`Docs/CP3_GATE1_CURRENT_BODY_RESIDENT_CACHE_CONTRACTS_COMPILE_HOTFIX_1_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP3 Gate 1 — Current Body Resident Cache Contracts

## Gate 1 implementation

- CP2.5 `FINAL CLOSURE / STANDARD PRELOAD ONLY`を唯一の実装原本として維持。
- `AERISCurrentBodyResidentCache`をMap DRAMとは別owner・別budget・別telemetryで追加。
- `INDEXED / SSD READY / DECODED / RAM RESIDENT / RENDER READY / GPU READY`の状態契約を固定。
- current body、environment hash、LOD、scope/body/database generationをcommit tokenで再検証。
- RAM budget、LRU eviction、lease式pin、scene/body/database遷移時の世代失効を実装。
- 固体表面を持たない天体またはenvironment fingerprint不成立時はfail-closedでinactive。
- Gate 1ではSSD read、decode、live RAM commit、描画、前方回廊、LAND、GPUへ接続しない。
- Map DRAMとTerrain Preload Databaseはpayload ownerへ変更せず、FULL BOOSTも復活させない。

詳細：`Docs/CP3_GATE1_CURRENT_BODY_RESIDENT_CACHE_CONTRACTS_v0.18.0.0_ja.md`

# AERIS Flight Control v0.18.0.0 DEV CP2.5 — Final Closure / CP3 Entry Baseline

## Standard Preload Only

- 手動FULL BOOSTを実行コード、UI、状態、テレメトリ、Scheduler方針から完全削除。
- 非Flight Terrain Preloadは、検証済みSTANDARDパイプラインへ一本化。
- STANDARDはPQS生成、CPU Encode、SSD Commitをbounded queueとcommit-required backpressureで処理。
- Terrain Blockはactive 64、pending block 4、outstanding 96を上限とする。
- CPU Encodeは最大32、SSD commit jobは最大1。完了通知は破棄しない。
- `required-drop=0`を維持し、標準パイプラインのstall／required-dropを検出した場合は保存済みDBを維持して自動復旧。
- Flight中はPreload Builderを停止し、Flight viewport／LAND安全処理を優先。
- ND／FDIのAUTO・ALWAYS・OFF、SPEED専用FDI、AIRFIELDS全0件カテゴリ無効化を維持。
- CP2.5 Track Aは本版で閉じる。Track Bの滑走路測地デフォルトは変更しない。
- CP3は本版を原本としてCurrent Body Resident Cacheへ移行する。

# AERIS Flight Control v0.18.0.0 DEV CP2.5 — Map DRAM Cache Foundation Hotfix 2


## Gate 4 Hotfix 2

- Airfield一覧、選択、Runway／ILS方向の通常読取りを共有Map DRAM snapshotへ実配線。
- 全Map DRAM対象同期I/Oをruntime guardへ通し、観測総数・許可された起動/保守I/O・通常lookup違反を分離集計。
- 終了時に`[CP2.5/MAP_DRAM_SUMMARY]`を出力。
- Terrain payload常駐は含まず、CP3境界を維持。

CP2.5 Track AのGate 4。Airport／Runway／ILS-Direction RegistryとTerrain Tile／LOD／chunk-location indexを、単一のrevision付きMap DRAM Cacheへ公開する。通常の地図検索はimmutable DRAM snapshotだけを読み、同期SSD検索を行わない。滑走路A/B絶対測地デフォルト（Track B）には変更を加えていない。

- Airfield Registryのatomic commit後にAirfield／Runway／Directionをdeep cloneしてpublish。
- Terrain preload manifestを起動時に読み、Tile／LOD／chunk位置メタデータをpublish。
- Terrain `Contains`／`TryGetChunkId`の通常経路をDRAM-only化。
- Repair／reindex／invalidation／manifest commit後にsnapshotを更新。
- DIAGNOSTICSへrevision、件数、推定DRAM、publish時間、hit/miss、SYNC SSD violationを表示。
- 圧縮Terrain、decode済み高度、mesh、RenderTexture、GPU objectは保持しない。本体RAM常駐はCP3。
- Gate 1 Altitude Gate、Gate 2 Quality Migration、Gate 3 LAND Separationを維持。
- Preload Builderと非同期payload workerは継続。
- CP2凍結識別子は回帰受入用の互換定数として保持。

# AERIS Flight Control v0.18.0.0 DEV CP2 — Absolute Geodetic Endpoint Authority Build Entrypoint Hotfix 1

This package supersedes Manual Runway Absolute Geodetic Endpoint Authority Hotfix 1. It fixes the build entrypoint rewriting the generated build identity to the previous Manual Runway Designation Grouping checkpoint before native Mono/xbuild compilation.

## Build Entrypoint Hotfix 1

- `build_ubuntu.sh` now regenerates the complete current identity ending in `MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1`.
- The generated C# identity, build-script identity and AVC metadata are synchronized.
- The build-time acceptance run can no longer fail at `167/168` because the latest suffix was erased immediately before xbuild.
- A dedicated regression test compares the identity surfaces and confirms that acceptance executes after current identity generation.
- Runway geometry, absolute A/B coordinates, certification, ND, LAND, Terrain, Auto Preload, AP, AA and APP behavior are unchanged.

# AERIS Flight Control v0.18.0.0 DEV CP2 — Manual Runway Designation Grouping Hotfix 1

This package supersedes Manual Calibration Reflection Hotfix 1. It refreshes manual-calibrated runway numbers from the committed A/B geometry and presents each physical runway as one AIRFIELDS item containing both reciprocal directions.

## Manual Runway Designation Grouping Hotfix 1

- Completed manual A/B calibration rewrites each direction label from `HeadingDeg` as `RWY NN`.
- The physical runway label is rebuilt as a deterministic reciprocal pair such as `RWY 09/27`.
- Stable IDs, geometry, cache identity and operational selection remain unchanged.
- AIRFIELDS lists one physical runway row instead of one row per reciprocal direction.
- The row format is `Airfield name` on the first line and `RWY NN / RWY NN` with length/status on the second line.
- Expanding the grouped row still shows both approach directions independently.
- Manual and automatic/provider categories remain separated and collapsed by default.
- No AP, AA, PROTECT, LAND control authority, Terrain, preload or propulsion logic was changed.

# AERIS Flight Control v0.18.0.0 DEV CP2 — Manual Calibrated Runway Separation / Preservation Hotfix 1

This package supersedes Responsive AIRFIELDS UI Layout Resize Hotfix 1. It fixes the AERISFlightControl(22) regression where a successful manual A/B runway pair could later be erased by the generic `CHECK HERE` quarantine path.

## Manual Calibrated Runway Separation / Preservation Hotfix 1

- Completed `UserCalibrated` A/B endpoint pairs are protected from automatic placement quarantine at both registry and storage layers.
- `CHECK HERE` is disabled for manual-calibrated entries. Replacing a manual pair requires explicit `CLEAR`, followed by `MARK A` and `MARK B`.
- AIRFIELDS now separates `CERTIFIED — AUTOMATIC / PROVIDER` from `USER CALIBRATED — MANUAL`.
- The manual category has its own persisted fold state and is closed by default.
- Manual-calibrated directions remain operationally selectable; only their presentation and destructive verification boundary changed.
- Existing settings migrate once with `airfieldsUiLayoutRevision = 2`.
- No AP, AA, PROTECT, LAND control authority, Terrain, preload or propulsion logic was changed.

This package supersedes the Bidirectional Runway Pair Hotfix 1 source checkpoint. It preserves the validated reciprocal runway calibration and fixes the ND runway layer disappearing after an airfield database revision, while restoring a readable AIRFIELDS page with every category closed by default.

## ND Navigation Snapshot Publish / Airfields UI Collapse Hotfix 1

- `CaptureNavigationSnapshot` now submits the captured runway/facility arrays to the shared `AERISNavigationDisplayPipeline` with body, generation, database revision and selection revision.
- Capture revision markers are committed only after the scheduler accepts the snapshot. A rejected submit retries after 0.5 seconds instead of suppressing refresh for 10 seconds.
- The compact main window no longer renders the complete accumulated hotfix identity. The full identity remains in logs; the UI shows semantic version and `DEV CP2` only.
- AIRFIELDS status/detail labels and runway rows use wrapped layouts. Runway identity and geometry/status are split across two lines.
- CERTIFIED, PROVISIONAL, FAILED, PENDING and REVALIDATION categories all default to closed.
- Existing settings are migrated once with `airfieldsUiLayoutRevision = 1`, so previously-open lists are also collapsed on first launch of this build.
- No runway recognition, certification, calibration, LAND, Auto Preload, AP, AA or APP control logic was changed.

This package supersedes Runway Witness / Anchor Scan / Calibration Hotfix 3. It preserves the CP2 terrain, preload, runway-witness and anchor-scan foundation, then adds an airport-independent field verification and removes the temporary runway survey display path.

## Final Candidate 3 Compile Hotfix 1

Mono/xbuildで`AERISAirfieldRegistry.VerifyRunwayPlacement`のローカル変数`stored`が、`witnessLibrary == null || ... out stored`の短絡評価経路では代入されないまま参照されるため、`CS0165`となる問題を修正した。`stored`を`string.Empty`で宣言時初期化し、Witness Libraryが無効な安全側経路でも既存の失敗メッセージへ確実にフォールバックする。滑走路位置判定、隔離保存、手動二点校正、Auto Preload、操縦権限には変更を加えていない。

## Final Candidate 3

`AERISFlightControl(18)`ではKola Islandが`AnchorSurfaceScan`で認証されていたが、実機位置と認証中心線の横ずれは約100.3mで、許容回廊約50.9mを超えていた。候補内部の整合だけではこの種の誤配置を完全には見抜けないため、空港名に依存しない実地点照合を追加した。

- AIRFIELDSの各滑走路に`CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY`を追加。機体を目視できる実滑走路中心線上へ停止させて実行する。
- 選択滑走路の両端から、長手位置、横ずれ、高度差を球面座標で算出する。
- 許容横回廊は`max(滑走路幅/2 + 12m, 中心線不確かさ×3 + 12m)`。長手端余裕は`max(100m, 幅×1.5)`。
- 機体が地上停止状態で、長手範囲と高度範囲に入っているのに横回廊外なら、空港名に関係なく配置不一致として永続隔離する。
- 隔離記録はAERIS所有の`PluginData/UserRunwayCalibrations.cfg`へ保存し、その滑走路providerを次回再測量から`UserCalibrationRequired`へfail-closed化する。
- 不一致を観測した場合は古い手動端点も無効化し、`MARK A`／`MARK B`の二点が揃うまでCERT、ND選択、LAND ARMへ戻さない。
- Kola Islandは既知の実機不一致としてcatalogでも`ManualRequired`へ変更した。これは既知事例の安全固定であり、汎用照合ロジックにはKola名・UUIDの分岐を含めない。
- Kramax Witness、Launch Anchor Surface Scan、Provisional安全境界、手動二点校正、Auto Preload Progressionを維持する。
- 一時的な滑走路候補オーバーレイ、候補ログ、設定トグル、一時DB経路、cache診断文字列を削除した。Provisional形状は非選択状態として残るがNDへ描画しない。
- 認証algorithmを1710、Runway Detector Revisionを5、User Calibration schemaを2へ更新。schema 1の既存手動校正は読み込み可能。
- 手動校正のprovider fallback照合は天体一致を必須化し、保存・検証失敗時はメモリ状態もロールバックする。`MARK A/B`は接地・停止中だけ受理する。

## 安全境界

`CHECK HERE`は観測・保存・再測量要求のみを行い、機体を操作しない。飛行中、着水中、5m/s超で移動中、滑走路長手範囲外、高度差過大の場合は判定を確定せず、隔離もしない。手動校正も座標証拠を保存するだけで、LAND操縦権限は追加しない。

## Mod Airfield Recovery Hotfix 1 + Auto Preload Progression 1

- KK／SLEの旧失敗cacheを対象限定で再測量し、独立舗装軸をmetadata primitiveで上書きしない。
- 完成済み現在天体が自動選択を占有し続けないようにし、全固体天体のFar coverage後は登録地点だけをLand detailへ昇格する。
- manual quality指定を自動昇格より優先し、環境hash変更時は完成markerを失効する。
- Hotfix 3はこの進行基盤を維持し、滑走路認証証拠だけを強化する。

## Compile Hotfix 1

Mono/xbuild の C# スコープ規則で `aspect` が重複して CS0136 になる問題を、`primitiveAspect` と `surfaceAspect` へ分離して修正した。滑走路軸推定ロジック、Preload Fast Path、操縦系の動作は変更していない。


## KK Runway Axis Reference Hotfix 2

`Axis Registration Hotfix 1`の実機退行を修正した。KK/SLE滑走路の物理舗装軸を、静的モデル本体のorientationではなく、独立したlaunch/spawn transform方位と広域照合する。舗装メッシュ軸は引き続き主証拠であり、launch方位は15°のfail-closed sanity gateにのみ使う。Axis Registration Revisionを2へ更新し、Hotfix 1が保存した`AbsolutePlacementInvalid`失敗キャッシュを対象滑走路だけ再測量する。

## KK Runway Axis Registration Hotfix 1

- KK／SLE滑走路の方位はLaunch Transformやprovider headingではなく、実配置済み滑走路面のMesh頂点から独立再測量。
- taxiway／apron／platform／obstacle／natural surfaceを軸推定から除外。
- 候補方位ごとに最密帯を抽出し、長手方向の被覆率・支持密度均一性でエプロン斜め帯を除外した後、滑走路床面だけでPCAを実施。
- 物理滑走路面の独立軸が得られない場合はCERT・LAND ARM・LOC／GSを拒否。
- provider／Launch headingは方位の正解として使わず、広いdesignator整合確認と診断テレメトリに限定。
- `[RUNWAY_AXIS]`ログへmesh heading、登録前後heading、補正量、designator誤差、支持点数、aspectを記録。
- KK／SLE専用axis revisionにより対象キャッシュだけを再測量。

## KK Runway Absolute Registration Hotfix 1

- Kerbal Konstructs／Stock Launchsites Expansion滑走路で、実配置Static originとLaunch Transformを別々の絶対配置証拠として取得。
- Mesh／Colliderから測定した中心線をLaunch Transformへ横方向拘束し、MOD空港の固定位置オフセットを補正。
- Launch Transformの方位、長手方向位置、最大補正量を検証し、不整合時はCERT・LAND ARM・LOC／GSを拒否。
- 認証アルゴリズム1680は維持し、KK／SLE専用Absolute Placement Revisionで対象キャッシュだけ再測量。
- `[RUNWAY_PLACEMENT]`ログへ補正前後の横誤差、長手位置、方位差、補正量を記録。


## Runway Presentation UI Hotfix 1

- 範囲外の選択滑走路を、地図中央基準の疑似滑走路ではなく自機アンカー基準の外周ポインターとして表示。
- ポインターへ滑走路名と現在距離を表示し、地図内の滑走路線と明確に区別。
- LAND下段を`ARM / CENTER / APP xx / SELECT / CLEAR`の順へ再配置。
- `APP xx`で同一滑走路の進入方向を選び、`SELECT`で確定。
- `CLEAR`はSELECTの右隣でLAND DISARMと空港・滑走路選択解除を同時実行。
- 狭いNDでは`CTR / RWYxx / SEL / CLR`へ自動短縮し、ボタンをパネル外へはみ出させない。


## Runway Map Lock Hotfix 2

- NDの横1.30倍・縦1.00倍という異方性スケール上で通常回転していたGPU地形を修正。
- `AERISNdMapProjection`へ球面投影、heading回転、GUI／RenderTexture上下方向、異方性補正を統合。
- 地形、海岸線、等高線、滑走路端点、選択判定、LOC表示が同じ変換を使用。
- 選択滑走路端点でGPU／GUI投影誤差を測り、1px超のGPU地形Commitを拒否。

## Preload Fast Path 1

- BALANCED／FAST／MAXIMUM速度プロファイル。
- 実測PQS sample costからframe budget内のsample数を動的決定。
- Preload BuilderはFinal-only Commitとし、25／50／75%の配列複製を省略。
- 隣接Tileの完全一致する境界PQS Sampleを、最大131,072点の有界Cacheで再利用。
- Cache hitはPQS query tokenを消費せず、隣接辺・共有角・互換LOD点の再照会を削減。
- 同じ8×8 Chunkへ完成Tileを最大8枚または350msまで集約して一括保存。
- Chunk round-trip検証とmanifest保存をBatchあたり一度へ削減。
- 最近解析したChunkを64MiB RAM Cacheへ保持。
- UI、ログ、CSVへFast Pathテレメトリを追加。

これはAERIS v0.18.0.0の**CP2実機検査用ソースチェックポイント**である。正式版、RC、LAND完成版、新NAV実装版ではない。

## 現在の到達範囲

- Gate 0：Physical Runway Canonical Federation、schema 7、control-free Adaptive Approach基盤
- CP1：ND更新レイヤー分離、固定6レンジ、TRACK UP／NORTH UP、PLAN／RECENTER、滑走路常時表示・選択
- CP2：Terrain Tile、LOD、GPU TOPO／REL、品質設定、TRAIL／VECTOR／TRAFFIC／WIND
- CP2修正：Mono C# definite-assignment、部分coverage描画、ND profile保存、最新viewport優先Terrain供給
- 今回の実機修正：Range／mode変更時の表示継続、Preview下敷き保持、有効三角形coverage、同一要求世代更新、AUTO負荷検知
- 今回：**Preload Terrain Map Builder、Preload Terrain Database、非同期並列読込み、Terrain Block Pipelineの一括統合**
- 小改良：**FMJ方式の単一常駐Toolbar ownerと、全主要sceneの読み取り専用Preload進捗画面**


## Runway Terrain Safety Hotfix 1 の変更

- MOD滑走路の登録Headingと閾値→反対端Bearingを照合し、180度逆の端点順を自動補正
- 補正不能な方向は認証、LAND ARM、LOC/GS、Runway Track Tokenを安全側で拒否
- 正しい進入側でない場合は`LOC N/A / GS N/A`とし、誤誘導数値を表示しない
- GPU TOPO／RELのShader UVパレット変換を撤去し、標高と機体高度から最終頂点色を明示計算
- 水面を固定青色化し、陸と水を別メッシュへ分離して海への陸色にじみを防止
- 水セルで等高線を停止し、海岸線を専用の太い帯メッシュとして描画
- `GPU AUTO`は能力確認付きCPU fallback、`GPU ON`は強制試行として区別
- 既存の`TERR Y DIRECT/FLIP`、`CLR SEL`、LAND線分クリッピング、coverage分離を維持

認証アルゴリズムは1680へ更新し、旧滑走路キャッシュを再認証対象にする。45秒周期stale cancellationと接地後の誤liftoff判定は別系統として意図的に変更していない。

## FMJ方式のPreload Status Toolbar

- Main Menu／Space Center／VAB／SPH／Tracking Station／Flightで同じAERISアイコンを使用
- 永続Bootstrap配下の単一`ToolbarBridge`がToolbarControlを一度だけ所有
- Launcher ready／destroyedで表示状態同期を無効化し、再生成後に安全に再同期
- duplicate Bootstrap／Toolbar owner／ToolbarControl生成を拒否
- 非FlightではPreload進捗・容量・対象天体・LOD・状態のみを表示
- 非Flight status画面にはBUILD／VERIFY／REBUILD／DELETE等の変更操作を置かない
- Flightでは既存AERIS Flight Controlウィンドウを開く
- Main Menu専用の別overlayボタンを作らず、FMJと同じToolbarControl経路へ統一

## 正式な地形供給構成

```text
主供給源
Preload Terrain Database
  非Flight中に生成・圧縮・永続保存
  Flight中は共有Schedulerで非同期読込み・復元

補完系統
Terrain Block Pipeline
  DBにない範囲だけPQSから小バッチ取得
  Block単位で段階表示
  Final完成後にPreload DBへ追加
```

Flight中に地形を毎回ゼロから生成する構成ではない。起動時は索引だけを読み、Tile Blobは必要になった時だけバックグラウンドで読む。

## 実装上の中核

- 非Flight Builder：OFF／MANUAL／IDLE ONLY／BACKGROUND／AGGRESSIVE IDLE
- 天体優先度：PINNED／HIGH／NORMAL／LOW／DISABLED
- 全球Overview優先、地域High／LAND Ultraを後段生成
- 独自のversioned binary index＋spatial chunk Blob
- UInt16量子化、Row predictor、Deflate、Raw fallback
- Water／Constant Height／Flat Tile短縮形式
- Tile・record CRC、journal、atomic replace、manifest backup、部分復旧
- Hot展開済みRAM／Warm圧縮RAM／Cold Disk／VRAM
- CRITICAL／HIGH／NORMAL／PREFETCH／BACKGROUND read lane
- viewport readがBuilder writeより常に優先
- stale generation拒否と旧Range／旧PLAN要求取消し
- Progressive Block CommitとCPU fallback上への部分HD合成
- 完成Previewを部分Finalの下に保持し、細密化中も表示coverageを後退させない
- Rangeによる等高線style変更中は直前の地形meshを暫定表示する
- display mode変更は頂点色だけを更新し、body-fixed Tile要求を無効化しない
- 既存Performance Runtime、共有Job Scheduler、bounded queue、generation、background I/Oを再利用
- ND専用ThreadPoolなし

## 安全境界

- AP／BANKは凍結
- 旧NAVは不在
- 新NAVはLAND完成までBLOCKED
- LANDは観測・計画・表示のみ
- ND／Terrain／BuilderはFlightCtrlState、MainThrottle、操舵へ書き込まない
- 圧縮ND地形をProtect最終安全判定の唯一の根拠にしない
- Tile待ちで自機、航路、滑走路、警報、TRACK UP更新を停止しない

## 静的受入

```bash
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_cp2_acceptance.py
```

実KSP試験は次を使用する。

- `Docs/ND_CP2_RUNWAY_MAP_LOCK_TEST_CARD_v0.18.0.0_ja.md`
- `Docs/ND_CP2_RUNWAY_TERRAIN_SAFETY_TEST_CARD_v0.18.0.0_ja.md`
- `Docs/CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX_1_v0.18.0.0_ja.md`
- `Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX1_ja.md`
- `Docs/CP2_FIELD_RENDER_CONSISTENCY_HOTFIX_1_v0.18.0.0_ja.md`
- `Docs/DEVELOPMENT_CHECKPOINT_v0.18.0.0_CP2_ja.md`

## 未実施

この作成環境にはMono/xbuild、KSP 1.12.5参照DLL、Unity実行環境がないため、ネイティブC#コンパイル、KSP起動、GPU描画、実PQS、長時間I/Oは未試験である。ユーザー環境のビルドと試験カードがCP2合否ゲートとなる。

## CP2.5 Gate 3 — LAND Separation Hotfix 1

LAND-resolution terrain is now controlled by a central data-only activation policy.
The developer capability toggle does not activate LAND during normal cruise. LAND
profile and selected-runway LAND requests require an active LAND ARM demand; future
Approach and Auto Landing inputs have reserved explicit demand slots. SSD Preload
Builder operation and per-body developer LAND preload controls remain independent.
## CP3 Gate 5 Candidate 12 Compile Hotfix 1

Inherited Candidate 11/12 selftests now validate historical lineage markers rather than incorrectly requiring an older candidate to remain the current UiCheckpoint. This fixes a pre-compile false failure; operational flight/terrain/ND/LAND logic and the Dessert RWY 36/18 field-verified default are unchanged.
