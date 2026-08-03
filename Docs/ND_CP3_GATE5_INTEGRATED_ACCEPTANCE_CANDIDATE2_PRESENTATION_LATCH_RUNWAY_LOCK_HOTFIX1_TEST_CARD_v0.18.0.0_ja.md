# AERIS v0.18.0.0 CP3 Gate 5 Candidate 2 実機テストカード

## 目的
Gate 5 Candidate 1で確認した「短いTerrainブラック/空白遷移」と「滑走路I字マーカーの浮遊追従」を再試験する。Candidate 2では最後に完成したGPU FRONTを無変形でラッチし、Terrainと世界固定シンボルを同一の確定projectionへ固定する。

## 必須確認
1. 起動直後は AIRPORT=NONE / RUNWAY=NONE。
2. TRACK UP、250〜350 m/sで80 kmと160 kmを各3分以上飛行し、FAR境界を複数回横断する。
3. 上記中にNDの黒抜け・Terrain消失・クソコラ化が0件である。
4. ログで `front=LATCHED` が出てもよい。`latch_age` は8秒以下で、通常は短時間でDIRECTへ戻ること。
5. 80/160 kmでは滑走路端threshold tickと滑走路端番号が表示されず、I字/H字マーカー化しないこと。
6. 5/10/20 kmでは滑走路番号（09/27等）が表示されること。
7. 滑走路を選択し、直進・旋回・range変更を行ってもTerrainに対して滑走路が浮遊/追従しないこと。
8. `[ND/TERRAIN_ALIGN] runwayMapLockErrorPx` は0.25 px以下を維持すること。Candidate 2では実際に表示中のFRONT projectionで測定する。
9. Terrain OFF→AUTOを実施する。GPU resource release後の再構築中は青系standbyで、CPU地形や黒いLAND背景による穴埋めをしないこと。
10. `cpu_terrain_draw=0`、`ready_build_violation=0`、GPU/DB/decode failure=0を維持すること。

## CP3 Gate 5 最終マトリクス
上記Hotfix再試験に合格後、Candidate 1のGate 5総合試験を継続する。
- 5/10/20/40/80/160 km
- TRACK UP / NORTH UP
- ND AUTO/OFF/AUTO
- Terrain TOPO/REL/OFF/AUTO
- 40 km altitude hysteresis
- LAND ARM/DISARM
- scene transition
- body transition（2天体以上）
- Flight 30分以上、推奨1時間

## 自動解析
`python3 Tools/analyze_v01800_cp3_gate5_runtime.py <AERISFlightControl.zip|log|directory>`

最終CP3閉鎖条件は自動解析 `OVERALL: PASS` と動画上の黒抜け0・浮遊滑走路0の両方。
