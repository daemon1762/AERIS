# AERIS v0.18.0.0 CP3.75 Candidate 3 Runtime Test Card

対象: `DEV CP3.75 — COASTLINE / CONTOUR PATH UNIFICATION CANDIDATE 3`

## 主目的

海岸線専用quad strokeを廃止し、等高線と同じline pathへ統一した結果、海岸線の可変幅／切り取り線artifactが消えるか確認する。

## 推奨条件

- HHC4
- Kerbin
- 高度 約30,000 m
- 速度 約2,100 m/s
- RANGE 160 km
- Terrain Quality: LOW (LOCKED)
- ND presentation: internal fixed 10 Hz

## 観察項目

1. 海岸線が場所によらず均一な細線である。
2. 局所的な帯、太い短冊、切断面、重なりがない。
3. 海岸線がland/water境界から離れない。
4. 等高線がCandidate2と同等に保たれる。
5. 高速直進・旋回で海岸線が消失／点滅しない。
6. TRK UP / NORTH UPで自機・地形・滑走路・predictionの整合を維持する。
7. `forced_recovery` がCandidate2同様に増殖しない。

## 判定

海岸線artifactが解消し、Golden地図品質とCandidate2 runtime stabilizationを維持すればPASS。
