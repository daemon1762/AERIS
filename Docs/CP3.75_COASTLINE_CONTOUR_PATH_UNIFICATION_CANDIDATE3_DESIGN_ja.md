# CP3.75 Coastline / Contour Path Unification Candidate 3

## 目的

Candidate 1/2 の実機試験で、等高線は Golden 水準で安定している一方、海岸線だけが場所により太さの異なる「切り取り線／帯」のように見える症状を確認した。

Candidate 3 はベースライン不具合潰しとして、海岸線の地理データ生成を変更せず、最終描画だけを等高線と同じ line path に統一する。

## 原因仮説

Candidate 2 までの海岸線は各 segment を独立した細長い quad に展開していた。この方式は segment の方向・join・重なりにより、局所的な幅変化や帯状 artifact を作り得る。

一方、等高線は `MeshTopology.Lines` を使う `BuildLineMesh()` 経路で、実機上の線形状が良好だった。

## Candidate 3 変更

- `BuildCoastlineMesh()` を廃止。
- `CoastlineHalfWidthNormalized` を廃止。
- `CoastlineSegments` をそのまま `BuildLineMesh()` へ渡す。
- 海岸線色 `Color32(185,225,255,245)` は維持。
- `BuildCoastlines()`、land/water clipping、coast crossing fraction、地理座標、projection、FRONT lifecycle は変更しない。
- contour renderer 自体も変更しない。

## 非目標

- 海岸線情報量の増減
- terrain sampling の変更
- coastline smoothing / reconstruction
- HIGH/MIDDLE 品質復活
- ND cadence変更
- FPS最適化

## 合格条件

1. 海岸線が等高線と同様の均一な線として見える。
2. 「切り取り線」「帯」「局所的な極太segment」が消える。
3. 海岸線位置は land/water boundary と一致する。
4. 等高線品質がCandidate2から退行しない。
5. 20 km / 160 km、TRK UP / NORTH UPで地図整合を維持する。
6. Candidate2のforced recovery抑制、10 Hz固定、LOW固定を維持する。
