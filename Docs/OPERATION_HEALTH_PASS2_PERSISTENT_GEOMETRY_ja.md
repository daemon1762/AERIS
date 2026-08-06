# Operation Health Pass 2 — Persistent Geometry / Allocation Reduction

## 目的
Candidate11〜Operation Health Pass 1 Hotfix 1の見た目・地形解像度・海岸線・等高線・10 Hz BACK cadenceを変更せず、CPU GCとUnity native Mesh生成破棄の負荷を削減する。

## 実装
- BuildEntry用のland/water SurfaceBuilderをmain-thread scratchとして再利用する。
- coarse land/water clippingで三角形ごとに生成していたSurfacePoint入力配列を廃止する。
- sparse coastal correctionでpolygonごとに生成していたCorrectionPoint配列を、worker build単位のscratchへ変更する。
- terrain triangle indexはList<int>成長→ToArrayの二重確保をやめ、同一判定を2-passしてexact-size int[]を1回だけ確保する。
- rasterizer CancelAllのscheduler-key Listを再利用する。
- Unity Meshは通常のtile supersession / eviction時に最大24個までbounded poolへ戻し、後続tileのland/water/coastal/contour/coastline Meshとして再利用する。
- Terrain OFF / viewport suspend / GPU resource releaseではpoolも全破棄する。VRAM release contractは維持する。
- Standard water colourで作成したMeshは初回Standard描画時の同一色再uploadを省略する。

## 変更しないauthority
- FAR基底33×33
- 129×129 high-density coastline/coastal class mask
- sparse coastal correction geometry
- Candidate11 contour budget 96 / coastal clip
- ARGB32 FRONT/BACK
- Bilinear filter
- BACK cadence 0.10 s
- projection refresh thresholds
- REL/TOPO palette
- Operation Health Pass 1 Hotfix 1のTerrain FRONT同期ownship / prediction / range fan

## Telemetry
`[CP3_GATE4C_VIRTUAL_DETAIL]` に以下を追加する。
- `oh_mesh_pool`
- `oh_mesh_pool_hit`
- `oh_mesh_pool_miss`
- `oh_mesh_recycle`
- `oh_mesh_destroy`
- `oh_surface_builder_reuse`

## Runtime acceptance
1. Candidate11/Pass1と比較して海岸線、fill、contour、REL色に視覚劣化がないこと。
2. 10/20 kmでownship/prediction/range fanのゲッタンが再発しないこと。
3. range変更と長距離飛行でGPU failure / missing tile / black frameがないこと。
4. tile入替が発生する飛行で`oh_mesh_recycle`と`oh_mesh_pool_hit`が増加すること。
5. Terrain OFFまたはviewport release後にpoolが永続的に保持されないこと。

## 既知の別課題
高倍率海岸付近で確認された局所的なfill塗り漏れはPass 2の最適化対象外。画質を変える修正とperformance最適化を混同しないため、別修正として扱う。
