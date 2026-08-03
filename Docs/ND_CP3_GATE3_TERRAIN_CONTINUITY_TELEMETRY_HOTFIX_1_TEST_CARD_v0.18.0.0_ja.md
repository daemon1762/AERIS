# ND CP3 Gate 3 Terrain Continuity & Telemetry Hotfix 1 実機テストカード

## 1. build表示

タブ上部が次であること。

`AERIS v0.18.0.0 DEV CP3 GATE 3 — TERRAIN CONTINUITY & TELEMETRY HOTFIX 1`

旧CP2またはHotfixなしGate 3が現在build表記として残っていればFAIL。

## 2. 黒い扇形の再現試験

Kerbin上を100～350m/sで飛行し、ND rangeを5/10/20/40/80/160kmの順および逆順で連続操作する。`HD TERRAIN BUILD xx%`中も黒い扇形・黒い矩形・一瞬の全面黒を表示しないこと。CPU fallback、互換する直前完成フレーム、または未確定地形下地のいずれかで連続表示されること。

## 3. range coalescing

350ms以内の連続操作では、ログの`[ND/TERRAIN] range=...m (coalesced)`が途中値すべてではなく最終確定値を中心に出ること。stale resultが操作回数相当で急増しないこと。

## 4. continuityログ

partial状態を維持した場合、次を確認する。

`[CP3_TERRAIN_CONTINUITY] coverage=...; visual=...; backing=COMMITTED_FRAME|CPU_FALLBACK|UNKNOWN_TERRAIN; age_ms=...; pending=...`

## 5. CP3 telemetry

10秒ごとに次を確認する。

`[CP3_TELEMETRY] body=Kerbin; ram=.../...; lod_gfrll=...; decode=.../...; corridor=...; req_pin=.../...; land=OFF|DEMAND.`

Performance CSVに`cp3_resident_*`、`cp3_corridor_*`、`terrain_continuity_*`列があり、各行の列数がheaderと一致すること。

## 6. AUTO品質分離

飛行開始直後のAirfield reload中に、滑走路snapshot作業だけを理由としてTerrain AUTO quality/rateがLOWへ落ち続けないこと。ログにhold開始と終了が1回ずつ記録され、その後Terrain自身の負荷に応じて通常判定へ戻ること。

## 7. 回帰

- ND滑走路位置、RUNWAY MAP LOCK、ILS漏斗が変化しない。
- LAND ARM/DISARMでGate 3のLAND demand/pin挙動が維持される。
- AA/AP/PROTECTの操縦挙動が変化しない。
- SSD/CRC/hash/decompress/GPU worker failureが0であること。
