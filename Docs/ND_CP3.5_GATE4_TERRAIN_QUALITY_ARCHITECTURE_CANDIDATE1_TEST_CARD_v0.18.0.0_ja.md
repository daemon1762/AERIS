# AERIS CP3.5 Gate 4 Terrain Quality Architecture Candidate 1 — Runtime Test Card

## 0. 前提
KSP 1.12.5、同一機体、同一地点、同一NDサイズで比較する。試験中にログと動画を保存する。

## 1. UI / identity
1. SYSTEM / OPTIONSでTerrain qualityが `AUTO / LOW / MIDDLE / HIGH` になっていること。
2. ビルド表示が `DEV CP3.5 GATE 4 — TERRAIN QUALITY ARCHITECTURE CANDIDATE 1` であること。

## 2. LOW baseline
1. 80 km程度でLOWを選択。
2. 地形が現Hotfix 1相当の33×33基盤として正常表示されること。
3. Ownship、prediction、runway、coastlineに位置ズレがないこと。
4. 15～20秒のFPSを記録。

## 3. MIDDLE — 33 real / virtual presentation
1. LOWと同じ位置・rangeでMIDDLEへ変更。
2. 海岸線、contour、斜め線のジャギー/輪郭がLOWより改善するか確認。
3. PQS負荷・tile生成がLOWから大幅増加しないこと。
4. FPSがLOWから大きく崩れないこと。
5. `[CP3.5_GATE4_QUALITY] effective=MIDDLE` を確認。

## 4. HIGH — bounded real65 / virtual129
1. HIGHへ変更し、最低30秒観察する。
2. `[CP3.5_GATE4_QUALITY]` の `high_refine_requested` がboundedであること。
   - <=40 km: 最大4
   - >40 km: 最大3
   - >80 km: 最大2
3. runtime負荷に余裕があれば `high_refine_completed` が増えること。
4. 海岸線/terrain contourがMIDDLEより改善するか確認。
5. real65生成中に33×33地形が消えたり、部分tileで穴が出たりしないこと。
6. 高負荷時にreal65が抑制されてもND表示自体は33基盤で維持されること。

## 5. Range regression
HIGHまたはMIDDLEで `160 → 80 → 40 → 20 → 10 → 5 km` を順に切り替える。
- Ownshipは正常anchorを維持。
- predictionは必ずownshipから伸びる。
- 異なるrangeの古いexact FRONTを誤表示しない。
- runway/world symbologyがterrainから分離して動かない。
- BUILDING時間を記録する。

## 6. Palette V3 regression
STD / RedGreenAssist / BlueYellowAssist / HighContrastを切り替え、Gate 3 Hotfix 1で確認した色差が維持されること。

## 7. 性能比較
同一条件で各15～20秒。
1. ND ON / Terrain ON — LOW
2. ND ON / Terrain ON — MIDDLE
3. ND ON / Terrain ON — HIGH
4. ND ON / Terrain OFF
5. ND OFF

平均FPS、最低付近、体感stall、ログ上のND main/PQS/worker値を保存する。

### Candidate 1で特に見るポイント
- MIDDLEはLOWとほぼ同じPQS負荷で見た目が良くなるか。
- HIGHは改善量に対して負荷が許容できるか。
- HIGHで2 FPS級cache thrashや30～40 FPS級の恒常的損失が再発しないか。

## 8. Restart / preload safety
HIGH使用後にKSPを終了・再起動する。標準preloadが異常肥大化せず、HIGH real65が永続化前提になっていないことを確認する。

## PASS判定
- Gate 3 Hotfix 1の座標/Palette修正を維持。
- LOW正常。
- MIDDLEで品質差が見え、重大な追加負荷なし。
- HIGHがboundedで、表示破綻・cache thrash・重大FPS collapseなし。
- runtimeログにGate4 telemetryあり。

## FAIL時に必ず添付
- `AERISFlightControl.log`
- `KSP.log`（可能なら）
- 動画または同一地点LOW/MIDDLE/HIGHスクリーンショット
- 使用rangeとquality切替順序
