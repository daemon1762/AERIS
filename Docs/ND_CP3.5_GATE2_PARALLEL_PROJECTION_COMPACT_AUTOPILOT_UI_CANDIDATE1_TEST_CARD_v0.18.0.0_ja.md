# AERIS v0.18.0.0 CP3.5 Gate 2 Candidate 1 実機試験カード

## 目的
Gate 1 Candidate 2で、160 km・約2100 m/s時のBACK生成約34 msのうち、CPU geographic projectionが約28 msを占めることを確認した。

Gate 2 Candidate 1では、地理投影と必要時の地形色計算をUnityメインスレッドからAERIS共有worker schedulerへ退避し、現在許可されているworkerを可能な限り並列使用する。メインスレッドにはUnity/KSP制約のあるMesh upload、RenderTexture、Material/Graphics、FRONT/BACK swapのみを残す。

これは専用GPU vertex shader版ではない。まず実機で「主犯約28 msをメインスレッドから外す」効果と安全性を確認するCandidateである。

## 1. ビルド
### デスクトップ Ubuntu
```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

### ノート Ubuntu
```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate2_ParallelProjection_CompactAutopilotUI_Candidate1_Source
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

## 2. 最重要性能試験 — 160 km / 約2100 m/s
1. Kerbin上でNDを160 kmへ設定。
2. TERRAINをON。
3. 約2000〜2100 m/sを目安に高速巡航。
4. 可能なら次の順で連続記録する。
   - ND OFF：15〜20秒
   - ND ON：30〜60秒
   - ND OFF：15〜20秒
5. 操作はなるべく少なくし、比較区間を安定させる。
6. `AERISFlightControl.log`と、可能なら動画を保存する。

### Gate 2ログ
`[CP3.5_GATE2_PARALLEL]`を確認する。

注目値：
- `submitted`
- `completed`
- `discarded`
- `admission_failed`
- `pending`
- `workers_last`
- `worker_cpu_ms_per_completed`
- `worker_wall_ms_per_completed`
- `projected_vertices`
- `colour_vertices`
- `target_cadence_s`

期待：
- multicore環境では通常`workers_last > 1`。
- `pending`は0または1のみ。2以上へ増えない。
- workerの合計CPU時間よりwall時間が大きく短くなるほど、複数コア並列化が効いている。
- `admission_failed`が継続的に増えない。

### 継承Profiler
`[CP3.5_GATE1_BACK_PROFILE]`も採取する。

Gate 1 Candidate 2比較値：
- BACK total：約34.1 ms中央値
- projection CPU：約28.0 ms
- bounds：約3.0 ms

Gate 2 prepared BACKの期待：
- `projection_cpu_avg_ms`：ほぼ0に近づく
- `bounds_avg_ms`：固定bounds化によりほぼ0に近づく
- `total_all_avg_ms`：Candidate 2から大幅低下
- `mesh_vertex_upload_avg_ms` / `colour_upload_avg_ms` / `draw_submit_avg_ms`はメインスレッド側に残る

## 3. 表示安全性
高速巡航中、TRACK UP / NORTH UP、レンジ変更を含めて確認：
- `forced_recovery=0`
- 黒い地形フラッシュなし
- phantom runwayなし
- terrain/runwayの位置ズレなし
- 自機・滑走路・地形のprojection authority不一致なし
- 古いFRONTをGUI.matrixで引き伸ばしたような表示なし
- NDが停止したまま復帰しない状態なし

## 4. CPU並列化の確認
今回の要件では、メインスレッドから退避可能な純計算は共有schedulerの現在許可workerを可能な限り使う。

確認ポイント：
1. `workers_last`がCPU/permit条件に応じて複数になる。
2. 高負荷時はAERIS schedulerの安全backoffでworker数が減ってもよい。
3. workerを増やすための独立スレッド乱立は行わない。
4. KSP/Unity API例外やthread affinityエラーがログに出ない。

## 5. AERISメインウインドウUI
### 上部
提示スクリーンショットを基準に確認：
- MASTER ARM：全幅の大ボタン
- `FLIGHT CONTROL / PROTECT / AUTOPILOT`：3等分
- `SYSTEM / EXTEND ADDONS`：2等分
- 左右の外側余白が同寸
- ボタン間隔が均等

### リサイズ
ウインドウを**最小 → 中間 → 最大 → 中間**へ変更する。

必須：
- ボタン文字が潰れない
- ボタン文字が見切れない
- ラベル/文章の勝手な自動改行なし
- 行構成が突然変わらない
- 片側だけ大きな空白ができない
- 1行横長ボタンはMASTER ARM小型版のような横長中央配置
- 1行ボタンの縦幅はコンパクト
- 滑走路一覧など意図的な2行ボタンは2行のまま読める

## 6. AUTOPILOT子カテゴリ
AUTOPILOTを開き、次の4項目を確認：

`TAKEOFF | FLIGHT | NAV | LAND`

- TAKEOFF：Auto Takeoff系
- FLIGHT：BANK / HDG / PITCH / V/S / ALT / ACC / VEL等
- NAV：航法/flight plan系
- LAND：進入・着陸系

状態色：
- そのカテゴリ内で1機能以上が有効 → 緑
- 全機能無効 → 赤

例：FLIGHT内でALTだけARMしてもFLIGHTは緑になること。

## 7. 返却データ
試験後、最低限次を送付：
- `AERISFlightControl.log`またはログZIP
- 可能なら試験動画
- 体感FPS差
- ND自体の滑らかさ
- UIの文字見切れ/潰れ/余白に問題があればスクリーンショット

## 8. 判定
PASSには、単にNDの更新頻度を落としてFPSを稼ぐのではなく、worker並列化で主犯の地理投影をメインスレッドから外し、表示品質・authority・UI可読性を維持しながらND ON時のframe impactが明確に減ることを要求する。
