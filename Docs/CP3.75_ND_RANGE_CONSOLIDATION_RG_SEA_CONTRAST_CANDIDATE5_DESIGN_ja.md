# AERIS CP3.75 Candidate5 — ND Range Consolidation / RG Sea Contrast

## 目的
Candidate4を安定ベースとして維持し、描画アルゴリズムを変更せずに不要な5 km ND rangeを廃止し、RG色覚支援presetで陸海識別性を改善する。

## Range authority
ユーザー選択可能なND rangeは次の5段階だけとする。

- 10 km
- 20 km
- 40 km
- 80 km
- 160 km

5 kmはUI非表示ではなくruntime authorityから完全削除する。旧`AERISSettings.cfg`や機体別`NavigationDisplayProfiles.cfg`に5 km相当の値が残っている場合は10 kmへ正規化し、可能な範囲で永続化する。

## RG sea contrast
RG (`RedGreenAssist`) のみ海面を深い青 `RGB(0,20,70)` とする。陸地、等高線、海岸線、滑走路、symbol色は変更しない。STD / BY / HIGHの海面色 `RGB(8,52,118)` も変更しない。

既存GPU tile meshはpalette切替時にwater vertex colourを更新し、cache再生成を要求しない。

## 非対象
- coastline geometry / sub-cell interpolation
- terrain sampling / preload
- contour generation
- forced recovery / presentation cadence
- prediction / LAND projection
- High-Density Coastline Preload
- GPU coverage drop / FPS最適化

これらはCandidate5では変更しない。
