# AERIS v0.18.0.0 CP3.75 — Pure ND Rebase Candidate 1 Runtime Test Card

## 目的

CP3.5のND実験系を捨て、Candidate14のND表示・地理関係・滑走路表示がAERIS20後発の非ND機能を維持したまま復旧したか確認する。**この候補ではFPS改善を合否条件にしない。**

## 事前確認

- UI title: `DEV CP3.75 — PURE ND REBASE CANDIDATE 1`
- startup airport/runway: NONE
- PRELOAD: ON/OFF policy
- KSC fixtureを基本とする
- selected runway有り/無しの両方を確認

## Gate C — Golden Visual

### 20 km
- CP3 Golden reference相当の等高線密度
- terrain reliefが読める
- coastlineが巨大階段/ブロック化しない
- coarse red polygon表示にならない

### 160 km
- Kerbinの陸塊・海岸線が地図として読める
- blue-only/blank terrainにならない
- whole-screen低情報upscaleにならない

## Gate D — Geographic Authority

各range `5 / 10 / 20 / 40 / 80 / 160 km` で確認。

1. 静止地上
2. 離陸～低速
3. 約300 m/s
4. 高速
5. heading変更

TRK UP:
- ownship、terrain、runway、predictionが一緒に回転
- range切替で各layerが独立移動しない
- short rangeほどownship driftが増えない

NORTH UP:
- 同じ地理関係が維持される
- runwayが消えない

## Gate E — Runway

- selected runway TRK UP: visible
- selected runway NORTH UP: visible
- terrain FRONT更新中でもrunway authorityを失わない
- phantom runwayなし
- physical runway 41 / reciprocal directions 82

## Gate F — Runtime

- exception/crashなし
- endless BUILDINGなし
- READYなのにFRONTが出ないgeneration chaseなし
- scene/body change後に復帰
- PQS無し天体をpreloadしない

## Gate G — Performance baseline（C～F PASS後のみ）

同一fixtureで各15～20秒以上:

1. ND ON / Terrain ON
2. ND ON / Terrain OFF
3. ND OFF

CP3時代の約40 FPS差が再現するかを記録し、その後の最適化targetを決める。

## 判定

- Gate C～FのいずれかFAIL → Candidate1 runtime FAIL、性能最適化へ進まない
- Gate C～F PASS → Pure Rebase成立。Phase 3 performance baselineへ進む
