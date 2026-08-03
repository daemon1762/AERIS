# ND CP3 Gate 4B ATTR 実機テストカード

## 1. ビルド
- Mono/xbuild成功
- タブ表記: `DEV CP3 GATE 4B — AERIS TERRAIN TEMPORAL RECONSTRUCTION (ATTR)`

## 2. 基本GPU-only
- 5/10/20/40/80/160kmを確認
- `cpu_terrain_draw=0`
- CPU terrain、CPU SAFETY FALLBACK、UNKNOWN_TERRAINが出ない

## 3. 安定飛行
40kmまたは80km、250～350m/sで60秒以上直進。
- `[CP3_GATE4B_TEMPORAL]`の`back_render`増加がRepaint FPSより大幅に少ない
- `swaps`が30回/秒級で増え続けない
- `back_skip`が増えるのは正常
- FAR表示欠落なし

## 4. TRACK UP旋回
40kmまたは80kmで左右360°旋回。
- `front=REPROJECTED`を確認
- `history_conf` > 0
- 旋回中の黒い扇形欠落なし
- 新BACK完成後`front=DIRECT`へ復帰

## 5. range変更
80→40→20→10→5kmのzoom-inを実施。
- 旧FRONTが全viewportを覆える遷移では`REPROJECTED`維持
- CPU fallbackなし

5→10→20→40→80→160kmのzoom-outも実施。
- 履歴が画面外地形を創作しない
- 履歴拒否時は`TERRAIN GPU BUILDING`を許容
- 新BACK完成後100% FARへ復帰

## 6. 高度ゲート / OFF
- 40km altitude hysteresis
- ND OFF / Terrain OFF
- scene transition
GPU resource解放後、再ON時にRender-Readyから再昇格できること。

## 7. ログFAIL条件
- `cpu_terrain_draw`が0以外
- stale terrain generationのhistory presentation
- FAR 100%未満BACKのswap
- GPU failure / CRC failure / hash mismatch / decompression failure
- 同期SSD read
- Flight safety lane占有
