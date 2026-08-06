# Operation Health Pass 3 Cadence Hotfix 2 / Refresh Coalescing

## 目的
Hotfix 1の10 Hz BACK/FRONT上限を、terrain presentation pipeline全体の10 Hz authoritative clockへ拡張する。KSP側Repaintが高FPSでも、CaptureVisible、worker completion drain、tile resolve/upload/schedule、foundation scan、projection判断、BACK renderは最大10回/秒だけ実行する。

## 構造
- authoritative tick間は既存FRONT textureを再提示するだけ。
- world-fixed symbologyは既存のFRONT swap同期を維持する。
- worker完了はqueueに蓄積し、次のauthoritative tickでbounded drainする。
- 同tick内の複数tile uploadは1つのgpuContentRevision dirty batchへcoalesceする。
- range/view invalidateは既存CancelAllを維持し、obsolete pending jobsを計測する。
- range/view変更はauthoritative clockをリセットせず、余計な即時frameを作らない。

## 非変更
Candidate11 cartographic visual authority、33x33 FAR基底、129x129 coastline authority、Sparse Coastal Correction、96-level contour budget、ARGB32/Bilinear、Pass 2 mesh pool、Pass 3 bounds/SetPass/index/colour optimization、FRONT-synchronized ownship/vector/fanは変更しない。

## 将来
この10 Hz exact-authority層を、GPU frame generation / temporal map reprojectionの入力基盤とする。
