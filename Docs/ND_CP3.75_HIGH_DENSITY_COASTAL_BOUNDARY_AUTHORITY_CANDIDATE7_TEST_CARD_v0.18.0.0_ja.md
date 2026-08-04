# CP3.75 Candidate7 実機テストカード

## 目的
Candidate6で高密度化された「線」だけでなく、陸/海fill境界も同じ129×129 coastal authorityへ統一する。
PRELOAD 100%は通常terrainだけでなくcoastal boundary生成完了まで含む。

## 初回起動
Candidate7は `PluginData/TerrainPreloadDatabaseV3` を新規Authorityとして使用する。
Candidate6以前の `TerrainPreloadDatabase` は参照しない。コピーもしない。

## PRELOAD合格条件
1. 通常terrain生成が終わってもcoastal phase未完なら100.0%を表示しない。
2. ocean天体は `[PRELOAD_COAST_HD] event=COMPLETE` 後に初めて100.0% / READYとなる。
3. ocean無し天体はcoastal workload無しで通常terrain完了後READYとなる。
4. BUILDINGが永久継続しない。

## ND合格条件
- 20 km: Candidate6で見えた巨大な33×33赤/黄fill階段が大幅に消える。
- coastlineとland/water fillが視覚的に同じ境界へ揃う。
- contour reliefはCandidate5/6と同じ品質を維持する。
- 160 km: coastline/landmassが地図として読める。
- TRK UP / NORTH UP、prediction、runway、forced recoveryに回帰なし。

## 注記
129×129はcoastal classification用。terrain relief/contourのheight authorityはLOW 33×33のまま。
