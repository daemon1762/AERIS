# AERIS v0.18.0.0 — CP3.5 ND Presentation Performance 再編ロードマップ

更新日: 2026-08-03  
対象: AERIS Flight Control v0.18.0.0 / CP3.5  
現在地: **Gate 2 — Exact Key Frame / Overscan Temporal Reprojection / Multicore GPU Candidate 2**

## 1. CP3.5 の目的

CP3.5 は、CP3 Gate 5 Candidate 14 までに成立した ND の機能・地形供給・滑走路 authority を維持したまま、**ND を表示した時のメインスレッド負荷を実用上無視できる水準まで低減する**ための性能アーキテクチャ checkpoint とする。

CP3.5 では「更新回数を落として FPS を稼ぐ」ことを最終解にしない。表示は滑らかに保ち、重い authoritative map generation と高頻度 presentation を分離する。

不変境界:

- CPU terrain raster/presentation へ戻さない。
- `GUI.matrix` による地形 temporal warp は使用しない。
- Runway Map Lock と全 world-locked layer の同一 projection authority を維持する。
- Current-Body Resident Cache / preload / predictive corridor の既存データ供給契約を原則維持する。
- Unity/KSP object (`Mesh`, `RenderTexture`, `Material`, `GameObject` 等) を worker thread から直接操作しない。
- worker は pure managed data、main thread は最小 commit、GPU は surface render / reprojection / composition を担当する。

---

## 2. 実機診断で確定したこと

### Gate 1 Candidate 2

160 km / 約 2100 m/s で BACK 生成約 34 ms のうち、CPU geographic projection が約 28 ms を占めた。`RecalculateBounds` 約 3 ms、vertex upload 約 0.7 ms、draw submission 約 0.5 ms。

結論: **全頂点をメインスレッドで毎回地理投影する設計が第一主犯。**

### Gate 2 Candidate 1 — Affine GPU Projection

BACK 生成は約 1.2 ms 級まで大幅改善し、方向性は正しかった。しかし 4 分割 Affine 近似には最大約 9 px 級の誤差が発生し、線状ちらつき・継ぎ目が確認された。また ND OFF でなお FPS が明確に上昇し、IMGUI Presentation 側に約 2 ms/frame 級の残負荷があることが分かった。

結論:

1. **近似 Affine を最終方式にしない。**
2. Exact geometry を worker で生成する。
3. Exact Key Frame 間は既知の地理運動から GPU Temporal Reprojection する。
4. 次段で static/world-locked ND layer を一枚の GPU surface に統合し、IMGUI repaint を削減する。

---

# 3. 再編 Gate

## Gate 0 — Performance Path Decomposition — **DONE**

目的: ND ON/OFF 差を constituent cost に分解する。

完了事項:

- BACK render / projection / mesh upload / bounds / draw submission / IMGUI repaint を分離計測。
- CPU projection が第一主犯であることを確定。
- worker terrain generation 自体は主犯でないことを確認。

完了条件: **達成済み。**

---

## Gate 1 — Cadence Integrity / Forced-Recovery Removal / Profiler — **DONE**

目的: 高速飛行時の hidden forced full-render loop を撤去し、再描画 cadence を観測可能にする。

結果:

- forced recovery bypass を除去。
- Candidate 1 の 0.50 s cadence 制限は ND 自体を約 2 fps にしたため、性能最終解として Reject。
- Candidate 2 profiler により 28 ms CPU projection を特定。
- UI の responsive/no-wrap/symmetric baseline を確立。

完了条件: **診断 checkpoint として達成済み。**

---

## Gate 2 — Exact Key Frame / Overscan Temporal Reprojection / Multicore GPU — **CURRENT**

### 目的

Affine 近似を撤去し、authoritative surface を**正確な地理投影**で作る。一方、毎 Repaint に全頂点を作り直さず、完成した Key Frame の間を GPU が高頻度で再投影する。

### Candidate 2 アーキテクチャ

**CPU multi-core producer**

- shared `AERISWorkerScheduler` の `GeneralCompute` lane を使用。
- permit 数と worker pool 数の範囲内で projection batch を vertex weight で分散。
- geographic unit point → exact projected vertex を worker 上で計算。
- terrain colour preparation も pure-data worker 側へ寄せる。
- 同時に outstanding batch は 1 個まで。buffer race と過剰 queue を禁止。

**Main thread commit**

- 完成 batch の `Mesh.vertices` / colour upload のみ。
- `RecalculateBounds()` は行わず、projection-safe bounds を使用。
- scheduler admission 失敗時も旧 full main-thread projection へフォールバックしない。最後の正確な FRONT を保持し、次 cadence で再試行する。

**GPU surface pipeline**

- `BACK`: 次 Exact Key Frame の描画先。
- `FRONT`: 完成済み authoritative exact key frame。
- `PRESENTATION`: 現在フレームへ temporal reprojection した表示面。
- 3 surface すべて ARGB32、bilinear、clamp。

**Overscan**

- Key Frame は通常 visible range の **1.25x** を描画。
- 160 km ND では 200 km surface を保持し、画面端に再投影余裕を確保。
- overscan は 250 km 上限。
- preload/cache policy 自体は変更せず、viewport capture の必要範囲のみ bounded に拡張。

**GPU Temporal Reprojection**

- Generic optical flow / AI interpolation は使用しない。
- CURRENT ND pixel → exact geographic lat/lon → FRONT key-frame UV を AERIS projection で算出。
- 8×8 continuous GPU reprojection grid を使用。
- 各 cell 中央で exact mapping と bilinear mapping の差を測定。
- **最大誤差 <= 0.75 px** かつ source UV margin を満たす時だけ PRESENTATION を許可。
- `GUI.matrix` は使用しない。

**Adaptive Exact Key Frame**

新 Key Frame 要求条件:

- terrain/content/body generation change
- range/surface range change
- temporal mapping unavailable
- interpolation error >= 0.30 px
- screen drift >= 36 px
- heading change >= 3°
- overscan edge margin 低下
- age >= 1.25 s

最短生成間隔: 0.35 s。表示更新自体はこの cadence に拘束されず、GPU temporal presentation は Repaint ごとに実行可能。

### Gate 2 受入条件

- Candidate 1 で見えた線状 flicker / affine seam が消える。
- `[CP3.5_GATE2_TEMPORAL] max_error_px <= 0.75` を維持。
- runway map lock <= 1 px。
- `forced_recovery=0`。
- main-thread full geographic projection path が通常・fallback とも実行されない。
- 160 km / 2100 m/s で exact key frame が worker pool へ分散される。
- ND 表示が key-frame cadence の 2–5 fps に見えず、連続的に移動する。

---

## Gate 3 — Temporal Visual Integrity / Adaptive Reprojection — **NEXT**

Gate 2 の基本方式を維持し、視覚破綻ゼロを目指す。

対象:

- 高速直線飛行
- 強い heading change /旋回
- TRACK UP ↔ NORTH UP
- 5 / 10 / 20 / 40 / 80 / 160 km range change
- 高緯度・経度 ±180° seam
- overscan edge 接近
- terrain generation rollover
- Kerbin → Laythe 等 body transition

候補改善:

- temporal grid を固定 8×8 から **誤差ベース adaptive subdivision** へ。
- error の大きい cell のみ 16×16 相当へ局所細分化。
- keyframe request の drift/error/heading threshold を range と速度から自動調整。
- PRESENTATION reject 時は black frame を出さず、最後の exact key frame を安全 latch。

Gate 3 完了条件:

- 線状 flicker、black wedge、surface tear、瞬間的な runway/terrain mismatch = 0。
- p99 reprojection error <= 0.75 px を実機ログで確認。

---

## Gate 4 — Unified ND World Surface / IMGUI Offload — **PLANNED / SECOND MAJOR PERFORMANCE GATE**

現在残る ND ON/OFF 差の本命。Gate 2 Candidate 1 実測では地形 BACK が軽くなった後も `nd_repaint_ema` 約 2 ms/frame 級が残った。

### World Surface へ統合する候補

- terrain fill
- coastline
- contour
- runway geometry
- airfield geometry
- route / flight-plan line
- range rings / static map marks
- world-locked approach geometry

これらを同一 projection authority で GPU RenderTexture に合成し、Exact Key Frame / Temporal Reprojection の対象へ含める。

### High-frequency Overlay として残すもの

- ownship symbol
- traffic / moving targets
- heading bug / dynamic vector
- selected/interactive cursor
- rapidly changing labels
- buttons / textual UI

目標: IMGUI は「巨大な地図を毎フレーム描き直す」のではなく、**world surface の `DrawTexture` + 軽量 overlay** に近づける。

Gate 4 完了条件:

- ND OFF と ON の main-thread frame delta を大幅縮小。
- static world layer の個別 IMGUI draw loop を原則撤去。
- runway hit-test/presentation authority は同一 exact projection を維持。

---

## Gate 5 — CPU/GPU Pipeline Refinement / Submission & Colour — **PLANNED / CONDITIONAL**

Gate 4 後の profiler で必要な項目だけ実施する。

候補:

- terrain colour の完全 GPU 化または worker差分化。
- CommandBuffer / draw batching。
- GPU surface composite の pass 数削減。
- Mesh upload spike の staging / chunk commit。
- scheduler permit tuning。
- producer/consumer を double/triple buffered に最適化。
- key-frame CPU worker と GPU submission の同期 wait 排除。

**測って必要なものだけ入れる。** Candidate 1 実測で `DrawMeshNow` submission は主犯でなかったため、blind batching は行わない。

---

## Gate 6 — Integrated CP3.5 Acceptance / Closure — **FINAL**

### 性能目標

暫定受入値:

- 通常 ND added median <= **2 ms/frame**
- 160 km / hypervelocity added median <= **3 ms/frame**
- 同 p95 <= **5 ms/frame**
- Key-frame generation が main-thread stall を発生させない。
- temporal grid CPU + GPU submit は低負荷で安定。
- ND OFF/ON差が Candidate 14 の 40 fps 級差から実用上小さい差へ縮小。

### 機能・視覚回帰

- Kerbin 160 km / 約 2100 m/s 長距離飛行
- Kerbin 一周
- 5/10/20/40/80/160 km 全 range
- NORTH UP / TRACK UP
- TERR AUTO / TOPO / REL
- ND ON/OFF
- runway select / airfield list / PLAN
- runway map lock <= 1 px
- phantom runway = 0
- line flicker / seam / black flash = 0
- Laythe transition
- surface-less body preload exclusion維持
- 41 physical runways / 82 directions baseline維持

### Closure artifacts

- full static regression
- clean unzip regression
- SOURCE/PACKAGE SHA-256 manifest
- ZIP CRC
- runtime test log/video evidence
- final CP3.5 acceptance document
-次 checkpoint への handover

---

# 4. 現在の再開位置

**Gate 2 Candidate 2 を実機ビルド・試験する。**

最優先試験:

1. Kerbin
2. ND ON / TERRAIN ON
3. 160 km
4. 約 2000–2100 m/s
5. 30–60秒直線飛行 + 軽い旋回
6. 最後に ND OFF 10–20秒

確認ログ:

- `[CP3.5_GATE2_PARALLEL]`
- `[CP3.5_GATE2_TEMPORAL]`
- `[CP3.5_GATE1_BACK_PROFILE]`
- `[CP3_GATE4C_VIRTUAL_DETAIL]`

Gate 2 Candidate 2 が視覚連続性を回復し、性能差がさらに縮小したら Gate 3 へ進む。残る主負荷が `nd_repaint` であることが再確認された場合、Gate 3 を短く閉じて Gate 4 Unified ND World Surface を主戦場とする。
