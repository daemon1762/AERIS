# AERIS v0.18.0.0 CP3.5 Gate 3 Candidate 2 — Presentation Authority Hotfix 1 実機試験カード

## 目的
AERIS58で確認した「内部ではFRONT/Temporalが成立しているのに、実画面のND地形が青一色または不可視になる」異常を切り分ける。
このHotfixではTemporal生成フレームを表示権限から隔離し、CP3 Frozen由来のExact FRONTを直接表示する。

## 第1関門 — 起動・可視性
1. KSPを起動しフライトへ入る。
2. ND ON / TERRAIN ON。
3. まず20 kmまたは40 kmで10秒待つ。
4. 160 kmへ切替える。

PASS:
- `TERRAIN GPU BUILDING`の後、一度Exact FRONTが完成したら地形が見える。
- その後、青一色へ戻らない。
- 海岸線・地形色が存在する場所で地形が継続表示される。

FAIL:
- Exact FRONT完成後も青一色。
- 数秒ごとに地形が消失する。
- READY_BUILDING違反が継続する。

## ログ確認
以下を確認する。

`[CP3_GATE4C_VIRTUAL_DETAIL]`
- `front=EXACT_LATCH`
- `direct_frames`が増加
- `exact_authority_frames`が増加
- `cpu_terrain_draw=0`

`[CP3.5_GATE2_TEMPORAL]`
- `presentation_authority=EXACT_FRONT_ONLY`
- `shadow_eligible`は増加してよい
- `authority_blocked`はHotfix中は増加してよい（Temporalを表示権限から意図的に隔離しているため）

## 第2関門 — 160 km高速
第1関門PASS後のみ実施。
- ND ON
- TERRAIN ON
- 160 km
- 約2000〜2100 m/s
- 30〜60秒

見る項目:
- 地図が継続して見えること
- 滑走路/airfield/world-locked layerが地図から浮かないこと
- 線状ちらつき、黒フラッシュ、青抜けがないこと
- FPSは記録するが、このHotfixでは最終性能合否にはしない

## 第3関門 — パレット
STD → RG → BY → HIGHを順に切替える。
- 地形が消えない
- High Contrastで黒欠落に見えない
- BYで白飽和しない
- RGで海陸が識別できる

## 提出物
- `AERISFlightControl`ログZIP
- 可能なら動画
- 160 km表示中のスクリーンショット1枚
