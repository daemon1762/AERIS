# AERIS v0.18.0.0 CP3.5 Gate 2 Candidate 2 Compile Hotfix 1 テストカード

## 1. ビルド確認
Ubuntu `build_ubuntu.sh` を通常どおり実行し、CS0120 が消失して DLL が生成されること。
ゲーム内タブ上部の checkpoint が `... CANDIDATE 2 — COMPILE HOTFIX 1` になっていること。

## 2. 実機回帰
このHotfixはcompile-only。ビルド成功後はCandidate 2本体の試験をそのまま継続する。
- ND ON / TERRAIN ON / 160 km / 約2000–2100 m/s
- 線状ちらつき、黒抜け、overscan境界、滑走路位置、Temporal Reprojection telemetryを確認
- UI: 左右均等、文字見切れなし、TAKEOFF | FLIGHT | NAV | LANDを維持

異常がなければ、Hotfix固有の受入はビルド成功でPASS。
