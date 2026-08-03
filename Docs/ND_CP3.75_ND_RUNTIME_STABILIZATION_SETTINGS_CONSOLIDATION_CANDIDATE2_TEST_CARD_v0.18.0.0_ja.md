# AERIS v0.18.0.0 CP3.75 — ND Runtime Stabilization / Settings Consolidation Candidate 2 Test Card

## Build identity
`DEV CP3.75 — ND RUNTIME STABILIZATION / SETTINGS CONSOLIDATION CANDIDATE 2`

## A. UI / Settings
1. SYSTEM > OPTIONSを開く。
2. Terrain qualityは `LOW (LOCKED)` だけ表示され、押して変更できないこと。
3. ND updateの選択行が存在しないこと。
4. 旧CFGがAUTO/HIGH/60等でも起動後runtimeはLOW/10 Hzへmigrationすること。
5. LAND着陸機能そのもの、FDR/CVR、AP/AA/PROTECT UIが消えていないこと。

## B. HHC4高速再現試験
- Kerbin
- hhc4
- 約30,000 m ASL
- 約2,100 m/s
- ND RANGE 160 km
- Terrainは通常表示、その他はデフォルト
- 60秒以上定常観察

### 必須確認
- late-CP3相当の等高線・陸塊・海岸線品質を維持。
- blue-only / blank / giant mosaic / coarse red polygon化なし。
- coastline strokeが連続し、フレームごとの消失/再出現がない。
- ownship iconに不自然なscale breathingがない。
- track vectorが高速で短縮崩壊しない。
- 2,100 m/s / 160 kmで30秒tickが表示される。45/60秒はrange外なら非表示でよい。
- selected runway/localizer funnelを表示した場合、FRONT latch中にも伸縮/浮遊しない。

## C. forced recovery gate
`AERISFlightControl.log` の `forced_recovery=` を確認する。

Candidate1のように定常高速飛行中5～6回/秒で増加するのはFAIL。
Candidate2では通常高速追従はscheduled refresh + FRONT continuityで処理されるため、forced recoveryはstartup/異常復旧レベルに留まること。目安として、FRONT READY後の60秒定常区間で増分5以下を期待する。

## D. Range / Orientation
5 / 10 / 20 / 40 / 80 / 160 kmを切り替え、各rangeでterrain/runway/ownship/vectorが独立して飛ばないこと。
TRK UPとNORTH UP双方を確認する。

## E. Performance observation
Candidate2ではFPSの絶対値はまだ最終合否ではない。ただしCandidate1と同条件で:
- `forced_recovery`増加率
- `nd_repaint_ema_ms`
- `frame_ema_ms`
を比較し、高速化に伴うAERIS由来の急増が減ることを確認する。

## FAIL条件
- Golden cartographic quality低下
- blank/blue-only/巨大モザイク
- coastline flicker継続
- track vector / localizer funnel伸縮継続
- forced recoveryが速度比例で連続増加
- runway/ownship/terrain projection不一致
- exception/crash/endless BUILDING
