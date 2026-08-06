# Operation Health Pass 3 Cadence Hotfix 1

## 目的
ND距離切替直後に ViewGeneration / gpuContentRevision の更新が 0.10 秒ゲートを迂回し、FRONT swap が一時的に 10 Hz を超える問題を修正する。

## 修正
- ViewGeneration / ContentRevision は refreshRequired の理由としてのみ使用する。
- 初回 FRONT 構築後の通常 BACK render は常に nextBackRefreshRealtime を通す。
- 初回 FRONT なしの bootstrap は即時許可する。
- blank 回避の forced recovery は既存の独立安全経路として維持する。
- oh_cadence_defer / oh_cadence_bootstrap を追加する。

## 非変更
Candidate11 visual authority、ARGB32/Bilinear、129x129 coastline authority、96-level contour budget、Sparse Coastal Correction、FRONT-synchronized symbology、Pass 2 mesh pool、Pass 3 bounds/SetPass/index/colour optimizationsは変更しない。
