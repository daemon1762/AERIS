# AERIS12 MOD Runway Survey Runtime Test Card

## 目的

現在のKK/SLE/KSC Extended滑走路が、実際の舗装面と一致するILS Geometryを生成し、非滑走路施設を表示・選択しないことを確認する。

## 起動時確認

- LAND欄の`SURVEY:`にCatalog/Matched/Measured/Manual/Rejectedが表示される。
- LANDリストにはRunwayだけが並ぶ。
- Helipad、RocketPad、LaunchPad、Harbour、WaterLaunchが並ばない。
- `MANUAL GEOMETRY REQUIRED`の施設は方向を生成せずARM不可。

## 滑走路確認

各自動測量滑走路について両方向を選択し、手動飛行で以下を確認する。

- ND中心線が実滑走路中心線と一致する。
- Thresholdが実舗装面の端付近にある。
- RWY番号が実進入方位と整合する。
- 反対方向が同一物理滑走路の逆向きである。
- LAND ARM中もCONTROLはPILOTのまま。

## ILS Runway-Only確認

- OVERLAY: Terrainは残るが、選択滑走路以外の施設記号は出ない。
- FOCUS: Terrainを消し、選択滑走路・LOC・GSだけが残る。
- 表示切替とRange/Zoomは従来どおり動作する。

## 不合格条件

- ヘリパッド等がILS候補になる。
- X字滑走路を一本の長大滑走路として自動生成する。
- 中心線が舗装面外を向く。
- 同一滑走路の両方向が別物理滑走路へ向く。
- LANDが操舵または推力へ書き込む。
