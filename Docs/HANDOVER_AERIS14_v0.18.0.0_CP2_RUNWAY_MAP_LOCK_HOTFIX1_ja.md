# AERIS14 引き継ぎ — CP2 Runway Map Lock Hotfix 1

## 現在地

CP2 Closure Candidate 1は実機で滑走路と地形のMap Lock不一致が再発したため撤回。本版はその根本修正であり、CP2はまだOPEN。

## 実機で確認された現象

- Island Airfieldの滑走路記号が、島の地形輪郭に固定されず時間とともに移動。
- 地形自体は自機中心へ追従していたため、滑走路側が動いて見えた。
- 実際の主原因は、粗いGPU地形Tile内部を四隅長方形へ押し込む投影誤差。滑走路記号は正確な地理座標であり、両レイヤーの投影が不一致だった。

## 修正

- GPU地形の全頂点を緯度・経度から現在ND中心へ球面投影。
- 四隅長方形TRSは描画に使用しない。
- 滑走路端点も現在ND中心から毎描画時に測地投影。
- マウス選択ヒットテストも同じ投影へ統一。
- 投影更新は中心移動0.25px相当、レンジ、anchor、天体変更時。
- 診断：`geometryProjection=SPHERICAL_VERTEX`。

## 維持

- KolaIsland等の滑走路方向安全検証。
- GPU TOPO／REL色修正、水面青、陸海分離、太い海岸線。
- LAND観測SnapshotのGeometry／Approach-sideコピー修正。
- AP/BANK/HDG/PITCH/V/S/ALT/ACC/VEL/Ground Stability凍結。
- LANDはcontrol-free、legacy NAV不在。

## 次の作業

`Docs/ND_CP2_RUNWAY_MAP_LOCK_TEST_CARD_v0.18.0.0_ja.md`を録画付きで実施する。全PASSした場合のみCP2をCLOSEDとして、CP3 Gate 6–8へ進む。

CP3予定：

- Gate 6：Approach Procedure Registry接続
- Gate 7：可変Glide Profile
- Gate 8：3D障害物Corridor
