# AERIS CP3.5 ND Presentation Performance Roadmap — Gate 3再編版

## 現在の目的
ND ON/OFFで残る数ms/frame級の差を、正確な地理表示・高解像度化・アクセシビリティを維持したまま縮小する。
CPU worker / main-thread minimum commit / GPU presentation の分離を基本契約とする。

## Gate 0 — Performance Path Decomposition — DONE
- CPU全頂点projectionが旧BACK生成の主犯と確定。

## Gate 1 — Forced Recovery / Cadence / Profiler — DONE
- forced full redrawを撤廃。
- cadenceを下げるだけではFPS改善にならず、NDだけ低fps化することを実機確認。

## Gate 2 — Exact Key Frame / Overscan Temporal Reprojection / Multicore — ARCHITECTURE PASS / PERFORMANCE PARTIAL
- 正確なkey frameをworkerで生成。
- overscan FRONTと地理ベースtemporal reprojectionを導入。
- 旧Affine patchの線状ちらつき経路を廃止。
- 残存負荷は主としてND Repaint/Presentation側へ移動したことを確認。

## Gate 3 — Unified World Surface / Adaptive Hi-Res / Accessibility — CURRENT
### 3A Unified World Surface Phase 1
- terrain/coast/contourに加え、world-locked runway/facility geometryをExact Key Frameへ統合。
- temporal reprojectionに同じsurfaceとして載せる。
- IMGUIはlabels/ownship/traffic/dynamic preview等へ縮小。

### 3B Adaptive High-Resolution Terrain
- ND画面pixelに対するscreen-space errorで FAR DIRECT / ADAPTIVE ROUTE / ADAPTIVE LOCALを選択。
- 33x33 FARを65x65/129x129へ連続再構成。
- 海岸カテゴリ境界もセル内部で連続化し、nearest-neighbour階段を削減。
- 中心近傍は実Route/Localをbounded progressive生成し、完成tileから置換。

### 3C Accessibility Palette Generation
- STD/RG/BY/HIGHを明度差込みで再設計。
- palette変更時はpending worker / exact FRONT / presentation authorityをgeneration invalidation。

### 3D Terrain Quality Simplification
- 描画品質LANDを完全廃止。
- AUTO / LOW / MEDIUM / HIGHのみ。
- LAND AP/着陸表示は独立機能として維持。

Gate 3終了条件:
- 160 kmで階段状海岸線が実用上改善。
- RG/BY/HIGHに欠落様黒潰れ・白飛び・海陸識別不良なし。
- world-locked geometryの座標ズレ/二重描画なし。
- ND ON/OFF frame差がCandidate 2より改善。

## Gate 4 — Full GPU Presentation Offload — NEXT
Gate 3後も `nd_repaint` が支配的なら実施。
- range ring / static route / airfield/world annotations等をさらにWorld Surfaceへ統合。
- temporal reprojectionをCPU 8x8 grid + GL quad群から、可能ならfull-screen GPU shader/1 quadへ移行。
- IMGUIをownship/traffic/text/input中心へ限定。

## Gate 5 — Pipeline Refinement — CONDITIONAL
計測上必要なものだけ実施。
- triple buffering / producer-consumer tuning
- upload staging
- terrain colour classification GPU化
- draw submission batching
- worker chunk sizing / backpressure tuning

## Gate 6 — CP3.5 Integrated Acceptance / Closure
- Kerbin 160 km / 約2100 m/s
- 全range / TRACK-UP / NORTH-UP / PLAN
- 高緯度・高速旋回
- Kerbin一周
- Laythe等別天体遷移
- runway map lock / phantom runway
- accessibility全palette
- 長時間稼働
- ND ON/OFF性能差
を総合受入してCP3.5を閉じる。
