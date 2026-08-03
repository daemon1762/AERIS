# AERIS v0.18.0.0 CP3.5 Gate 1 Candidate 2 実機試験カード

## 目的
Candidate 1で`forced recovery`自体は止まった一方、160 kmの0.50秒cadenceがND地形を約2 fpsへ落としただけで、1回のBACK描画コストが残ったことを受けた診断Candidate。

Candidate 2では全レンジを0.20秒の明示cadenceへ戻し、4回に1回だけBACK内部を詳細計測する。最終目的は、この結果をGate 2 GPU Geographic Projection / submission改修へ渡すこと。

## 1. ビルド
### デスクトップ Ubuntu
```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

### ノート Ubuntu
```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate1_BackRenderProfiler_SymmetricTopUI_Candidate2_Source
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

## 2. 最重要試験 — 160 km / 約2100 m/s
1. Kerbin上でNDを160 kmへ設定。
2. 約2100 m/sの高速巡航を維持。
3. ND OFFを10〜20秒、ND ONを30秒以上、ND OFFを再度10〜20秒観察。
4. 可能なら動画と`AERISFlightControl`ログを保存。

### 必須ログ
`[CP3_GATE4C_VIRTUAL_DETAIL]`で以下を確認：
- `forced_recovery=0`
- `back_cadence_s=0.20`

`[CP3.5_GATE1_BACK_PROFILE]`を最低3本採取する。
注目値：
- `total_all_avg_ms`, `total_all_max_ms`
- `projection_cpu_avg_ms`
- `mesh_vertex_upload_avg_ms`
- `bounds_avg_ms`
- `colour_cpu_avg_ms`, `colour_upload_avg_ms`
- `draw_submit_avg_ms`
- `other_avg_ms`
- `projected_vertices_avg`, `draw_calls_avg`

**最大の項目をそのままGate 2の第一改修対象候補とする。**

## 3. 表示整合性
高速巡航・レンジ変更・TRACK UP/NORTH UPで以下を確認：
- 黒い地形フラッシュなし
- terrain/runwayのズレなし
- phantom runwayなし
- 古いFRONTのGUI.matrixワープに見える伸縮なし

## 4. AERISメインウインドウ上部UI
提示スクリーンショットを基準に確認する。

1. MASTER ARMが上部を左右均等な余白で広く使用する。
2. 1段目は`FLIGHT CONTROL / PROTECT / AUTOPILOT`の3等分。
3. 2段目は`SYSTEM / EXTEND ADDONS`の2等分。
4. 各段の左余白と右余白が同じに見える。
5. ボタン間隔が均等。
6. ウインドウを**最小 → 中間 → 最大 → 中間**へ連続リサイズ。
7. リサイズ途中でボタンが突然別座標・別段へ飛ばない。
8. ボタン文字、ラベル、説明文が勝手に折り返されない。長い場合はclipされる。
9. SYSTEM内ではDIAGNOSTICSを復活させず、PRELOAD MAPSが残りの行幅を左右均等に使用する。

## 5. 判定
このCandidateは性能最終版ではない。PASS条件は、0.50秒低fps回避を撤回でき、forced recoveryを再発させず、BACKコスト内訳を信頼できる形で取得でき、指定UI外観/対称性が成立すること。
