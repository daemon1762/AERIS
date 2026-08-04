# CP3.75 Coastline Sub-Cell Interpolation / Line Cache Accounting Candidate 4

## 目的

Candidate 3 で海岸線の可変幅・短冊状 artifact は解消したが、160 km では海岸線中心線そのものに階段状の形状が残った。
Candidate 4 は Golden/LOW の terrain sampling を増やさず、既存サンプルの land/water 境界位置をサブセル内でより正確に求める。

同時に Candidate 3 で coastline を quad から `MeshTopology.Lines` へ変更した後も残っていた旧 quad 前提の cache byte accounting を修正する。

## 変更

1. terrain sampling が water 判定に使用する `elevation <= 1.0 m` を coastline crossing の共通閾値とする。
2. land/water の2サンプルが 1.0 m を挟む場合、線形補間でサブセル交点を求める。
3. legacy/reconstructed sample が閾値を正常に挟まない場合は Candidate 3 / Golden の固定 38% crossing へ fallback する。
4. coastline polyline と land/water fill clipping は同一 `AERISTerrainCoastlinePolicy` crossing を使用する。
5. coastline の Entry cache accounting を、廃止済み quad 展開相当の4倍計上から raw float segment 1回分へ修正する。

## 維持事項

- Candidate 3 の `BuildLineMesh()` / `MeshTopology.Lines` coastline presentation。
- Candidate 2 の Golden LOW 固定、ND 10 Hz 固定、forced recovery 抑制。
- 33x33 Golden baseline sampling。品質プリセット復活はしない。
- coastline source data の分類そのもの、PQS preload、projection authority。
- AA/AP/PROTECT/LANDING/Recording 等の非ND protected scope。

## 非目標

- coastline の高解像度再サンプリング。
- spline / Catmull-Rom 等による人工 smoothing。
- HIGH/MIDDLE 復活。
- FPS の大規模最適化。
- Factory Terrain Seed 同梱。

## 合格条件

1. Candidate 3 の均一線幅を維持する。
2. 160 km の海岸線で固定38%由来の階段感が軽減する。
3. coastline と land/water fill boundary が一致する。
4. blue-only / blank / land bleed を発生させない。
5. Candidate 2/3 の runtime stabilization を維持する。
6. coastline cache accounting が line topology と一致する。
