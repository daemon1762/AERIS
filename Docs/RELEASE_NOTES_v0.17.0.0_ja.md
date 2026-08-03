# AERIS Flight Control v0.17.0.0 リリースノート

状態: **Performance Runtime Source RC**  
日付: 2026-07-23  
基準: Precision Coding #1後のv0.16.4.0 Consensus Runway Certificationソース

## 結論

安全クリティカルな操縦・所有権・PROTECT・LAND最終判定をMain Thread／決定論的CPU経路に残したまま、計算、表示前処理、記録、アーカイブを共通の有界ランタイムへ移しました。

旧NAVは復元していません。独立LANDは引き続き認証・表示・観測専用で、FlightCtrlStateやAP出力を所有しません。基準BANK則はSHA-256一致で不変です。

## 新規ランタイム

### CPU scheduler

- AUTO AGGRESSIVE既定値は`max(2, logicalProcessors - max(2, ceil(logicalProcessors × 0.15)))`
- 明示worker数に固定12上限を設けない
- Safety/LAND 64、General `max(32, CPU×4)`、Telemetry 128、Archive `max(8, CPU)`の有界queue
- 10スロットの重み付き公平制御
- LAND中はSafety予約を最大2 permitへ拡大
- frame P95、AERIS Main P95、Safety delay、Writer backlog、GPU時間で即時後退
- 安定15秒ごとにpermitを1つずつ回復
- ArchiveはLAND／高負荷時にpause
- Main Threadから`.Wait`、`.Result`、`Join`を使用しない

### 世代・latest-wins

非同期結果は次を識別します。

- Scene generation
- Vessel persistent identity／instance generation
- Body
- Control point
- Docking／separation
- Runway database／selection revision
- Flight plan revision
- Display layout revision
- sample sequence／monotonic timestamp

同一keyの待機ジョブは置換し、実行中の旧ジョブはcommit時に拒否します。完了result queueも256件で有界化し、同一keyの新しい結果だけを保持します。

## 計器・Terrain・Runway

- AERIS canonical snapshotを20 Hzで発行
- AIFD向け公開APIは同一のmode、deviation、alert、LAND状態を提供
- Pitch ladder／heading ticksをCPU workerで前処理
- 公開配列は`IReadOnlyList`＋`Array.AsReadOnly`で不変化
- Terrain rasterを共通schedulerへ移管
- 不正dimension、非有限高度、64 MiB超RGBA allocationを拒否
- Runway certification workerを共通schedulerへ移管
- 最終certification commit、Approach Freeze、stagingはMain Threadに維持

## GPU境界

- Compute Shader能力とdeviceを診断
- GPU OFF受入モードを追加
- GPU不在／無効／登録失敗時はCPU fallback
- GPUを最終安全判断やRunway certificationの唯一の根拠にしない

本RCには実Compute Shader／AssetBundleを含めていません。従って、GPU許可時も通常は`COMPUTE CAPABLE — CPU FALLBACK ACTIVE`です。これはGPU性能受入の合格を意味しません。

## Logging／FDR／CVR

- `AERISLogger`の`File.AppendAllText`を除去
- FDR／CVR／各診断CSVのMain Thread `StreamWriter`を除去
- 数値・timestamp・quote処理をWriter threadへ移動
- 8192 command＋64 control予約の有界FIFO
- Critical EventはVerbose、次にContinuousを退避して優先
- drop sequenceのfirst／lastと種別countを記録
- Open／Close／write／flush failureをflight controlから隔離
- dropまたはwrite failureを含むsessionはZIP化せずraw保持
- 障害streamのDisposeをproducer queue lock外へ分離
- percentile配列のcopy／sortをproducer queue lock外へ分離
- 拡張telemetry channelは128件、field 128件、0.2～50 Hzへ制限
- CSV header正規化後の重複、`utc`／`ut`衝突、ファイル名衝突を拒否

既存FDR／CVR headerとcolumn順はPC1 byte-identical selftestで維持しています。

## FlightData archive

- 共通Archive laneでZIP生成
- 同時Archiveを1件へ直列化
- `*.zip.tmp`へ作成後、全entryをraw sourceと展開バイト単位で比較
- 同一サイズの破損も拒否
- 検証成功後のみfinal ZIPへmoveし、raw folderを削除
- scheduler拒否、queue full、cancel、writer failureではrawを保持
- 起動時recovery scanは新規open sessionを明示的に除外
- vessel changeごとの再scanを廃止し、recorder instanceごとに一度だけ実行

## 設定と診断

次回起動用設定を追加しました。

```text
performanceWorkerOverride = 0      // AUTO。2以上で明示指定
performanceGpuAccelerationEnabled = True
```

`SYSTEM > OPTIONS`からAUTO／2-WORKER TEST、GPU許可／OFFを選べます。診断画面にはCPU、workers、permits、lane別active／queue、delay、worker P95、AERIS Main／snapshot／commit P95、Writer、Archive、GPU、degradation reasonを表示します。

Snapshot capture時間は捕捉した1フレームだけに計上します。旧実装候補のように20 Hzサンプルを後続フレームへ反復計上しません。診断値は1 Hzでcacheし、表示中の毎フレームsortを排除しています。

## インストーラー

旧方式のMODディレクトリ全削除を廃止しました。更新するのは配布管理対象のPlugins、Icons、既定FlightPlans、既定Airfieldsです。次を保持します。

- `Config/AERISSettings.cfg`
- ユーザーFlightPlans／Airfields
- AA学習／設計データ
- FlightData
- Logs

## 受入結果

- v0.17静的検証: 145/145
- build entrypoint: 11/11
- generation／scheduler model: 34/34
- async recording／archive: 67/67
- PC1／display／safety invariants: 45/45
- PC1 consensus runway cases: 15/15
- reload／LAND freeze／AIRFIELDS／ND: 53/53
- legacy NAV removal: 44/44
- foundation data／display: 40/40
- FDI／ND layout: 79/79
- internal manifest: 3/3
- 合計: 536/536、11/11 scripts（ソース／モデル本体533/533）
- C# tree-sitter構文解析: 108/108 files
- `build_ubuntu.sh`: `bash -n` PASS
- csproj XML parse: PASS
- BANK SHA-256: `bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7`

## 未実施／合格を主張しない項目

- xbuild／msbuild／dotnet／csc／mcs（作業環境に存在しない）
- KSP `Assembly-CSharp.dll`参照による実コンパイル
- KSP実起動、J-1／HHC-4飛行
- 2-worker／GPU OFFの実機基本機能
- AUTO AGGRESSIVEのCPU使用率とMain Thread P95比較
- 長時間FlightData、disk full／permission denied／forced termination
- Linux／Windowsおよび各graphics API
- true allocation profilerによる0 B/frame
- 実GPU Compute
