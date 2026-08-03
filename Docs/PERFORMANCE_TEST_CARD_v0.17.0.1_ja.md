# v0.17.0.1 実KSP性能・安全受入テストカード

## 目的

静的／モデル試験では確認できない、KSP上の型互換、worker scheduling、frame time、allocation、I/O fault、Scene／Vessel lifecycleを判定します。

このカードを完了するまでv0.17.0.1を実行時合格にしないでください。

## 0. 前提

- KSP 1.12.5の試験用コピー
- ToolbarController 0.1.9.12以降
- J-1とHHC-4、可能なら高part数機体
- 同一save／同一graphics／同一physics deltaで旧PC1と比較
- OS、CPU、logical processor数、RAM、GPU、driver、graphics APIを記録
- 既存`GameData/AERISFlightControl`をバックアップ

## 1. ビルド

```bash
python3 Tools/run_v01701_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "/absolute/path/to/Kerbal Space Program"
```

合格:

- acceptance 11/11 scripts
- xbuild Release成功
- DLLが`GameData/AERISFlightControl/Plugins`へ導入
- Config、FlightData、Logs、ユーザーFlightPlans／Airfieldsが保持
- KSP logにloader exception、type load、missing methodがない

## 2. 最小構成 — 2 workers／GPU OFF

`SYSTEM > OPTIONS`で:

1. `2-WORKER TEST`
2. `Allow optional GPU assist after restart`をOFF
3. KSPを完全再起動

Diagnosticsで確認:

- `WORKERS 2`
- GPU `GPU DISABLED — CPU FALLBACK`
- `active_permits`は2以下

機能試験:

- MASTER ON／OFF
- BANK、HDG、PITCH、V/S、ALT、ACC、VEL
- AIRFIELDS startup load
- manual reload／連打coalesce
- LAND ARM／DISARM、Overlay／Focus
- Terrain ND
- FDI／ND move／resize／reset
- Vessel change、scene exit／entry
- FDR session終了と次session開始

合格:

- 基本機能が停止しない
- MASTER OFFが即時
- stale resultがscene／vesselを越えて表示／commitされない
- LAND ARM中のfrozen DB／geometry revisionが不変
- legacy NAVが表示／起動しない

## 3. AUTO AGGRESSIVE

`AUTO WORKERS`、GPU許可ONで完全再起動します。

確認:

- `configured_workers = max(2, logical - max(2, ceil(logical×0.15)))`
- 固定12で頭打ちにならない
- 高負荷時permitが1または2ずつ減る
- 安定15秒ごとに1 permitずつ回復
- LAND active時Safety予約とArchive pauseが働く
- Writer backlogで`WRITER BACKLOG BACKOFF`

合格目安:

- Main Thread worker wait 0回
- queueがcapacityを越えない
- Safety queue delay P95 < 8 ms、Max < 20 msを目標
- AERIS Main P95がPC1より悪化しない
- Runway／Terrain result ageが表示更新を破綻させない

ハードウェア差があるため、絶対値だけでなくPC1同条件比較を残してください。

## 4. Runway決定性

同一inputで次を各3回実施します。

- startup load
- manual reload
- reload連打
- LAND ARM中reload→DISARM後commit

比較:

- record count
- certified／failed／pending／revalidation数
- stable ID
- geometry fingerprint
- threshold／heading／length／width
- failure code

合格:

- worker completion orderに関係なく結果一致
- startupとmanualの同一入力結果一致
- LAND ARM中にselected geometryが差し替わらない
- unresolved ambiguityを推測認証しない

## 5. Scene／Vessel stale試験

Runway、Terrain、Instrument jobがpending／runningになる負荷を作り、直後に:

- Vessel切替
- docking／undocking
- control point変更
- Flight scene退出
- game scene再入場
- display layout reset

合格:

- 旧vessel／旧body／旧runway／旧layout結果がcommitされない
- queue depthが時間とともに回復
- cancelled／staleは増えてよいがfailedが連続増加しない
- disposed texture／object access exceptionがない

## 6. FDR／CVR長時間

最低60分、推奨3時間:

- 50 Hz BANK diagnostics有効
- AP modeを周期的に変更
- extension telemetryを0、1、最大channelで実施
- Vesselを複数回切替

合格:

- control loopにfile I/O spikeがない
- header／data column数一致
- sequence dropがない、またはdrop count／first／lastが明示
- session名衝突なし
- Close後にZIPが生成され、全raw entryと一致
- raw削除はZIP検証成功後のみ

## 7. I/O fault injection

試験用コピーでのみ実施します。

- Logs／FlightDataをread-only
- disk quota／空き容量不足
- archive destinationに既存invalid ZIP
- session write中に権限変更
- KSP通常終了
- OSによる強制終了

合格:

- flight control／MASTER OFFが動作
- Writer failureがDiagnosticsへ出る
- failure sessionはraw保持
- 次回起動recoveryが現在open sessionをarchiveしない
- corrupt／same-size-different-content ZIPを採用しない
- partial `.tmp`をfinalとして扱わない

## 8. Allocation／Profiler

Unity Profilerまたは同等手段で、次を別々に採取します。

- UI全閉、AP OFF、水平飛行
- FDI／ND表示
- Diagnostics表示
- FDRのみ
- Runway reload
- Terrain update

記録:

- GC.Alloc/frame
- GC collection interval／pause
- Update／OnGUI／FixedUpdate CPU
- snapshot capture／commit drain
- Writer encode／disk／flush

判定:

- `gc_positive_delta_ema_bytes`だけで0 B/frame判定をしない
- 通常飛行0 B/frameは目標であり、実Profilerが0を示した場合のみPASS
- 本RCでallocationが残る場合は値とcallsiteを記録し、隠さない

## 9. GPU

本RC単体では実Compute Shaderがないため、次だけを判定します。

- 非対応GPUでCPU fallback
- GPU許可OFFでCPU fallback
- capability probe例外で起動継続
- GPU欄がActiveでないことを正しく表示

実GPU backendを追加した将来版では別途:

- Linux OpenGL／Vulkan、Windows DirectX
- device loss／shader failure
- CPU exact resultとの一致
- AsyncGPUReadbackを通常安全経路に使用しない
- GPU停止時にも操縦／認証が成立

## 10. 飛行安全

J-1、HHC-4で:

- 低速／低Q
- 高速
- 高高度
- BANK 20°捕捉とhold
- mode切替
- LAND表示中reload
- logging failure中MASTER OFF

合格:

- BANK roll-in、pre-brake、zero-rate capture、steady holdを維持
- micro-wobble／overshootがPC1より悪化しない
- worker／GPU／Writer exceptionがcontrol authorityを保持しない
- invalid sensor値で非有限commandを出さない

## 11. 証拠一式

- KSP.log
- Player.log
- `performance_runtime.csv`
- 該当FlightData ZIP／raw
- AIRFIELDS画面capture
- SYSTEM Diagnostics capture
- PC1対比表
- profiler capture
- 使用したDLL SHA-256
- source ZIP SHA-256

すべての項目を満たして初めて`PC2_Acceptance = PASS`としてください。
