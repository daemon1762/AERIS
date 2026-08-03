# AERIS14 引き継ぎ — v0.18.0.0 CP2 Runway Terrain Safety Hotfix 1

## 原本

`AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayTerrainSafetyHotfix1_Source.zip`

## 再開位置

CP2 Runway/Terrain Safety Hotfix 1のnative Monoビルドと実機検査。

## 今回の重大修正

1. MOD滑走路のHeadingと閾値端点方向の180度逆転を自動補正。
2. 補正不能な滑走路方向を認証・LAND ARM・Track Tokenから拒否。
3. 正しい進入側以外ではLOC/GSをN/Aにする。
4. GPU TOPO/RELのUVパレット方式を撤去し、標高・機体高度に基づく明示頂点色へ変更。
5. 陸と水を別メッシュに分離し、水を固定青色化。
6. 水セルで等高線を止め、海岸線を専用の太い帯メッシュに変更。
7. GPU AUTOを能力確認付きCPU fallback、ONを明示的強制試行として区別。

## 安全境界

- LANDは観測・表示専用。
- AP/BANK/HDG/PITCH/V/S/ALT/ACC/VELを変更しない。
- 新NAVはBLOCKED。
- 不整合滑走路方向は将来の自動着陸APIへ渡さない。

## 未実施

作成環境にはMono/KSP/Unity参照環境がないため、native compileと実GPU描画は未実施。静的受入とクリーン再展開受入を配布前に実行する。

## 次の合否ゲート

- KolaIsland RWY34等でLOC漏斗が実滑走路の正しい進入側へ一致。
- 反対側ではLOC/GS N/A。
- RELが機体高度上昇に従い赤→黄→緑→暗緑へ変化。
- TOPO標高色が正常。
- 水面固定青、陸色にじみ無し、海岸線が等高線より太い。
- AP/BANK回帰無し。

