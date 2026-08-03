# AERIS v0.18.0.0 CP3.5 Gate 3 Candidate 1 実機試験カード

## 対象
Unified World Surface / Adaptive Hi-Res / Accessibility Candidate 1

## A. 最初のスモーク試験
1. Kerbinで離陸し、NDをON、Terrain GPUをON/AUTOにする。
2. 5 / 20 / 40 / 80 / 160 kmを順番に切り替える。
3. 地形、海、滑走路、空港記号が同一座標系で表示され、黒抜け・phantom runway・大きな線状ちらつきがないこと。
4. 160 kmで海岸線が旧33x33拡大より滑らかになり、時間経過で中心付近の詳細が段階的に改善すること。

## B. 性能試験（最重要）
同一機体・同一視点で各20〜30秒。
- ND ON / Terrain GPU ON / 160 km / 可能なら約2000〜2100 m/s
- ND ON / Terrain GPU OFF
- ND OFF

提出物: `AERISFlightControl.log` と可能なら動画。
確認ログ:
- `[CP3.5_GATE1_BACK_PROFILE]`
  - `total_all_avg_ms`
  - `world_surface_avg_ms`
  - `world_surface_primitives_avg`
- `[CP3.5_GATE2_TEMPORAL]`
- Performance telemetry の `nd_repaint_ema_ms`, frame time

目標: World Surface統合によってrunway/facilityの毎Repaint IMGUI幾何描画が減り、ND ON/OFF差がCandidate 2より縮小すること。

## C. アクセシビリティ
Terrain coloursを `STD -> RG -> BY -> HIGH -> STD` と切り替える。
- HIGHで安全地形が黒く欠落したように見えない。
- BYで注意帯が白飛びしない。
- RG/BY/HIGHすべてで海面と陸地を明度でも区別できる。
- 切替直後に旧色と新色が同一地図内で混在しない。
- ログに `[CP3.5/ACCESSIBILITY] palette generation ...` が出る。

## D. 高解像度・progressive refinement
- 160 km MEDIUM/AUTO: まず完全coverageが出て、その後中心近傍のRoute精度が追加されても表示停止しない。
- HIGH: 中心近傍のrefinement範囲が拡大しても長時間のメインスレッドstallを起こさない。
- range変更時に古い解像度surfaceが位置ずれして残留しない。

## E. LAND品質廃止確認
SYSTEM > OPTIONS > Terrain quality は `AUTO / LOW / MEDIUM / HIGH` の4個のみ。
過去設定から起動してもLAND品質は復活しない。
注意: AUTOPILOTの `LAND`、LAND進入/縦断表示、LAND profile sizeは別機能なので存在して正しい。

## F. UI回帰
- AERIS上部: MASTER ARM、3分割、2分割が左右均等。
- AUTOPILOT: TAKEOFF / FLIGHT / NAV / LAND。
- 最小サイズまで縮めてもボタン文字が潰れない・勝手に改行しない。
