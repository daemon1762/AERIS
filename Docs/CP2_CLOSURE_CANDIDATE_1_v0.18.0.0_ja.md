# AERIS v0.18.0.0 DEV CP2 Closure Candidate 1

## 目的

CP2実機受入で残った二点を閉じる候補版。

1. MOD滑走路が認証・ARM済みなのに、NDキャッシュ上だけ`RUNWAY GEOMETRY INVALID / GS N/A`となる状態伝達欠損を修正。
2. GPU地形の塗りむらと海岸付近の陸色はみ出しを低減。

## LAND修正

`AERISNavigationDisplay.CloneObservation()`が次の二状態を複製していなかった。

- `OnApproachSide`
- `RunwayGeometryDirectionValid`

このため、実観測では正常でもND用スナップショットだけ既定値`false`となっていた。両状態を含む全誘導判定を複製し、KolaIsland等の認証済みMOD滑走路で正しいLOC／GS表示を維持する。

## 地形品質修正

- 陸水境界をグリッド中点から陸側38%位置へ保守的に寄せる。
- 不確実な海岸セルは陸色ではなく水面側へ割り当てる。
- 塗りメッシュと海岸線を同じ三角形・同じ交点から生成する。
- RELは警戒色を優先し、陰影を極小化する。
- TOPOも陰影範囲を狭め、三角形状の濃淡むらを抑える。

## 安全境界

既存の滑走路Heading整合検査、LAND ARM拒否、Track Token拒否、反対進入側でのLOC／GS N/Aは維持する。LANDに操縦権限は追加しない。
