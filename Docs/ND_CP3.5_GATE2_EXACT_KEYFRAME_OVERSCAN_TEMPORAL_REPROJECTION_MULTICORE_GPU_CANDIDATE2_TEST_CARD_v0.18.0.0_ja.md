# AERIS v0.18.0.0 CP3.5 Gate 2 Candidate 2 — 実機試験カード

対象ビルド: **DEV CP3.5 GATE 2 — EXACT KEYFRAME / OVERSCAN TEMPORAL REPROJECTION / MULTICORE GPU CANDIDATE 2**

## 目的

1. Affine Candidate 1 の線状ちらつき/継ぎ目が Exact Key Frame 方式で消えたか確認する。
2. exact projection が multicore worker で処理され、main thread full projection fallback が発生しないことを確認する。
3. 1.25x overscan FRONT から GPU Temporal Reprojection した ND が key-frame cadence に見えず滑らかに動くことを確認する。
4. ND ON/OFF の FPS 差がさらに縮小したか測定する。
5. Runway Map Lock、phantom runway、black flash を回帰確認する。

## A. ビルド確認

ゲーム内 AERIS タイトルに次を確認:

`DEV CP3.5 GATE 2 EXACT KEYFRAME OVERSCAN TEMPORAL REPROJECTION MULTICORE GPU CANDIDATE 2`

ビルド失敗時はコンソール全文を保存し、飛行試験へ進まない。

## B. 主試験 — 160 km / hypervelocity

- Kerbin
- ND: ON
- Terrain: ON/AUTO
- Range: **160 km**
- 速度: **2000–2100 m/s 目安**
- 30–60秒以上維持
- 直線飛行の後、可能なら緩い左右旋回を追加
- 最後に ND OFF 10–20秒

目視:

- 地形が滑らかにスクロールする。
- 旧Candidate 1で見えた線状ちらつき・patch seamがない。
- 黒い帯/三角形/black wedgeがない。
- 海岸線/contourが瞬間的に裂けない。
- runwayとterrainが別々に動かない。
- ND OFF時にFPSが上がる量を体感または録画で比較する。

## C. Temporal stress

可能なら同一飛行で以下を各数秒:

- TRACK UP ↔ NORTH UP
- 160 → 80 → 40 → 160 km
- heading 20–60°程度変更

合格目安:

- 切替直後に一瞬古い exact key frame を安全保持することは許容。
- black frame、線状破綻、runway mismatchは禁止。
- transition後は新 key frameへ収束する。

## D. UI回帰

AERISウインドウを最小/中間/大きめに変更。

- ボタン文字が見切れない。
- 自動改行しない。
- 左右余白が均等。
- MASTER ARM → 3分割 → 2分割の上部構成が崩れない。
- AUTOPILOT配下が `TAKEOFF | FLIGHT | NAV | LAND`。
- 各カテゴリは一つでも機能ONなら緑、全OFFなら赤。
- 1行横長ボタンは薄型mini MASTER形式。
- 意図的な2行runwayボタンは2行のまま。

## E. 重要ログ

### `[CP3.5_GATE2_PARALLEL]`

見る値:

- `submitted`
- `completed`
- `admission_failed`
- `workers_last`
- `worker_cpu_ms_per_completed`
- `worker_wall_ms_per_completed`
- `projected_vertices`
- `colour_vertices`

期待:

- `workers_last` > 1 が実機で確認できる条件では multicore fanout 成立。
- completedが継続して増える。
- admission failure が連続しない。

### `[CP3.5_GATE2_TEMPORAL]`

見る値:

- `frames`
- `rejects`
- `keyframe_requests`
- `max_error_px`
- `min_uv_margin`
- `drift_px`
- `heading_delta_deg`
- `grid_cpu_ms_per_frame`
- `submit_ms_per_frame`
- `confidence`

期待:

- accepted presentation中 `max_error_px <= 0.75`。
- grid/submitが小さい。
- framesがkey frame completedより大幅に多く増える。
- rejectsが常時増え続けない。

### `[CP3_GATE4C_VIRTUAL_DETAIL]`

期待:

- `forced_recovery=0`
- `cpu_terrain_draw=0`
- runway map lock 警告なし

## F. 提出物

最小:

- `AERISFlightControl.log`

推奨:

- 飛行動画
- ND ON/OFF比較を含む動画
- UI最小/中間/大のスクリーンショット

## Gate 2 Candidate 2 判定

**PASS候補:**

- Affine由来線状ちらつき消滅
- temporal error <=0.75px
- multicore exact keyframe生成成立
- forced recovery 0
- runway lock正常
- ND表示が滑らか
- FPSがCandidate 1以上に改善

**FAIL:**

- 線状ちらつき残存
- black wedge/black flash
- runway/terrain mismatch
- temporal error超過表示を継続
- main thread stall再発
- ND ON/OFF差が悪化
