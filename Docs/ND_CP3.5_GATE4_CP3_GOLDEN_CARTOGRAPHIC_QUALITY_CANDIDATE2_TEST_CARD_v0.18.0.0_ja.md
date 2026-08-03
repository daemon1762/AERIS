# AERIS ND CP3.5 Gate 4 Candidate 2 実機テストカード

## 0. 前提
- KSP 1.12.5でnative build成功を確認。
- 同一機体・同一場所・可能なら同一カメラ条件で比較する。
- Golden Visual Referenceはユーザー提示のCP3最終盤スクリーンショット3枚。
- 画質判定は「設定名が変わった」ではなく、実際の海岸線・等高線・地形連続性で行う。

## 1. LOW — 最重要品質下限
Terrain=LOW / TRACK UPで、5/10/20/40/80/160 kmを各15秒以上。

PASS:
- 20 km: CP3最終盤相当の細かい輪郭/contourが見える。
- 40/80 km: 巨大な赤/黄ブロックや階段状セル塊が主表示にならない。
- 160 km: 海岸線・島・陸塊が地図として明確に読める。
- FAR foundation ready後の青一色/blankが0。

FAIL:
- Candidate 1と同じ巨大ポリゴン塊。
- CP2初期相当の粗い赤形状。
- 地形の恒久消失。

## 2. MIDDLE
同一地点/同一rangeでLOW->MIDDLE。

PASS:
- CP3品質floorを維持。
- 近距離はLOCAL97、20..80 kmはROUTE65、長距離も再構成地図を維持。
- LOWよりedge/contour/rasterの表示が同等以上で、ボケた単純bilinear拡大に見えない。
- PQS負荷がLOWから大幅増加しない（追加PQSを要求しない設計）。

## 3. HIGH
同一地点/同一rangeでMIDDLE->HIGH。

PASS:
- REAL65がまだ無くてもCP3 Golden fallbackが全面に残る。
- `high_refine_completed`増加後、対象範囲がVIRTUAL129相当に精細化する。
- partial REAL65で表示が欠けない。
- 80/160 kmで全画面REAL65/129生成によるcache thrashを起こさない。

## 4. FRONT安定性
160->80->40->20->10->5->20->80->160 kmと連続切替。

PASS:
- 自機アイコン固定。
- prediction line/tickが常に自機起点。
- wrong-range FRONT流用なし。
- range変更後に一時BUILDINGがあっても、FAR ready後に恒久blue-onlyへ落ちない。
- 安定viewportで`CP3_GATE4B_READY_BUILDING_VIOLATION`が0。

## 5. 連続旋回
20 kmまたは40 km、TRACK UP、250–350 m/s程度で360度以上連続旋回。

PASS:
- coastline/contour/terrain fillが互いにずれない。
- ownship/map-lock driftなし。
- 地形の裂け/黒穴/青抜けなし。

## 6. パレット
STD / RG / BY / HIGHをLOWとHIGHで切替。

PASS:
- Palette V3の差が維持。
- 海陸判別可能。
- black crush / white saturation / stale palette mixtureなし。

## 7. 性能比較
同一条件、各20秒程度:
1. ND ON / Terrain LOW
2. ND ON / Terrain MIDDLE
3. ND ON / Terrain HIGH
4. ND ON / Terrain OFF
5. ND OFF

画質を最優先するが、MIDDLEの追加PQSは原則0、HIGHのREAL65はboundedであること。HIGHで2 FPS級cache thrashへ戻った場合はFAIL。

## 8. 提出物
- `AERISFlightControl`ログZIP
- 画質比較動画
- 可能ならLOW/MIDDLE/HIGHの同一地点スクリーンショット

ログ重点:
- `[CP3.5_GATE4_QUALITY]`
- `virtual_builds`
- `high_refine_requested/completed/partial_suppressed/safety_skips`
- `CP3_GATE4B_READY_BUILDING_VIOLATION`
- FRONT state / latch age / drift_px


## 同梱Golden Reference
`Evidence/CP3_GOLDEN_VISUAL_REFERENCE/` に受入基準の原画像3枚を同梱する。runtime判定ではこの原画像と同一rangeの画面を直接比較する。
