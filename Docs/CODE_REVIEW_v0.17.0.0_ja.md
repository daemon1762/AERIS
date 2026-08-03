# v0.17.0.0 全体コードレビュー

## 判定

**Source RCとして合格。実KSP RCとしては未判定。**

理由:

- Performance Runtimeの境界、queue、世代、故障隔離は静的／モデル試験で整合
- PC1のRunway、LAND freeze、FDI／ND、FDR schema、旧NAV削除を回帰試験で維持
- BANK基準則はbyte-identical
- 一方、KSP参照DLLとC# compilerがなく、ネイティブ型検査、実行、飛行、性能測定は未実施

## 監査範囲

108 C#ファイルと、csproj、GameData CFG、build script、全selftestを対象に確認しました。

- Core lifecycle／Scene／Vessel transition
- CPU scheduler／generation／commit
- Runway certification／reload／Approach Freeze
- Terrain sampling／raster／ND upload
- AERIS／AIFD canonical data
- GPU capability／fallback
- Logger／FDR／CVR／extension telemetry
- FlightData archive／recovery
- AA background training thread／telemetry
- PROTECT sensor validity
- settings persistence／SYSTEM options
- installer／user-data preservation
- legacy NAV absence／LAND control independence

## アーキテクチャ境界

| 領域 | Main Threadに残すもの | Workerへ渡すもの | commit条件 |
|---|---|---|---|
| Flight control | ownership、最終control law、MASTER OFF、PROTECT | なし | 同期実行 |
| LAND | phase、ARM、freeze、最終認証commit | plain runway snapshot／geometry input | 全generation一致 |
| Terrain | PQS／CelestialBody sampling、Texture2D upload | float elevation、byte flags、dimension | generation＋grid revision |
| Instruments | canonical flight-state capture、GUI描画 | primitive snapshot、projection前処理 | generation一致 |
| Logging | immutable field capture | CSV encode／disk write | FIFO sequence |
| Archive | seal request | path、byte stream、ZIP verify | raw exact-byte一致 |
| GPU | capability probe／将来dispatch | safety外の補助候補のみ | CPU exact validation必須 |

Worker実装ファイルには`Vessel`、`CelestialBody`、`Texture2D`、`GameObject`、`FlightCtrlState`、`UnityEngine.Object`を含めない静的検査を設定しています。

## schedulerレビュー

### 合格点

- 全laneが有界
- 同一lane／keyはlatest-wins
- 待機中置換、実行中置換、完了順逆転の各ケースを処理
- 完了queueも有界かつkey indexを同時に除去
- generation-bound resultはMain Thread commit直前に再確認
- Safety待機中はnon-safety active数を制限
- LAND active時に予約permitを増やしArchiveをpause
- stop時に待機job、result、latest indexを無効化
- Main Threadはworker終了を待たない

### 追加修正

`ActivePermits`、`SafetyReservedPermits`、`ArchivePaused`はpolicy lockで更新され、worker側から別lockで読まれていました。`volatile` backing fieldへ変更し、permit policyの可視性を明示しました。

### 残留リスク

- `Thread` schedulingはOS／Mono依存であり、優先度は保証ではない
- 2 worker時のSafety freshnessは実KSP負荷で要測定
- commit callbackはlatest判定と不可分にするためscheduler lock内で実行する。callback長時間化を実測監視する必要がある
- cooperative cancellationの粒度は各workerの`ThrowIfStale()`位置に依存

## generationレビュー

Equality／hash／matchには全state revisionを含め、sample sequenceとtimestampだけをstate compatibilityから除外しています。これにより同一状態の新sampleはlatest-winsで置換でき、Scene／Vessel／Runway等の変更は旧結果を拒否します。

Runway database／selectionは外部revisionをそのまま同期し、LAND ARM中の実geometryは既存Approach Freeze cloneで固定します。staged reloadはDISARMまでlive geometryを交換しません。

## Runtime telemetryレビュー

記録項目:

- logical processors、configured workers、active permits
- lane別active／queue
- queue delay P50／P95／Max
- worker／commit／Runway／Terrain／Instrument P95
- result age
- submit／complete／replace／drop／cancel／stale／fail
- frame／AERIS Main／snapshot capture／commit drain EMA・P95
- GPU active／backend／frame time
- Writer depth／drop／failure／encode／disk／flush
- Archive pending／complete／fail／deferred／duration
- GC positive heap delta EMA proxy
- degradation reason

### 計測誤差修正

20 Hz snapshot capture costを後続のcaptureなしフレームへ再利用していたため、適応policyが過剰後退し得ました。capture発生時のみsampleへ追加し、commit drainは毎frame独立計測、AERIS Mainは当該frameの和として記録するよう修正しました。

UIが`SnapshotTelemetry()`を毎frame呼ぶと、P95配列copy／sort自体がGCとCPUを発生します。runtime／writer snapshotを1 Hzでcacheし、DiagnosticsとCSVが同じcacheを読むよう変更しました。

`gc_positive_delta_ema_bytes`は`GC.GetTotalMemory(false)`の1 Hz正方向差分であり、Unity Profilerの`GC.Alloc/frame`と同義ではありません。0 B/frameの合否には使用できません。

## Writerレビュー

### queue／優先度

- total 8192、control予約64
- Critical、Continuous、Verbose
- control commandはdataを退避して優先
- Critical dataはVerbose、Continuousの順で退避
- critical-only飽和時はdropを隠さずsessionをunsealableにする
- shutdownはStopの後ろへ新規writeを受け付けない

### 順序／seal

Open、Write、Flush、Close、Sealは一つのFIFOで処理します。Seal callbackはそれ以前の全Close後にのみ実行されます。Open／Closeを含むdrop、write／flush／dispose例外はfolderをfailedにし、archive callbackを実行しません。

### 追加修正

- Close成功前にchannel pathを削除しない
- Open／Close control dropもunsealableにする
- failure処理で`StreamWriter.Dispose()`をproducer queue lock外へ移動
- Writer percentile copy／sortをproducer queue lock外へ移動
- Stop queue admission raceを閉鎖

### 残留リスク

- CSV field arrayはcaptureごとに確保される
- Writer threadはencodeとdiskを直列実行し、handover理想案のparallel block encodeまでは実装していない
- disk hangを強制中断する安全なportable APIはなく、background threadが長時間残る可能性がある。ただしownership releaseは待たない
- OS強制終了時の最後のbufferは失われ得る

## Archiveレビュー

### 合格点

- Archive laneと単一I/O coordinatorで同じfolderへの競合を回避
- queue 64、result 256で有界
- symlink／reparse pointを収集対象外にする
- source root外へescapeするpathを拒否
- temporary ZIPを全entry展開しrawと完全バイト比較
- entry count、name、duplicate、length、contentを検査
- validation後のみfinal move／raw delete
- cancel、scheduler rejection、queue fullではraw保持
- recovery scanは新規open folderを除外

### 残留リスク

- archive中に外部プロセスがrawを変更するとvalidation failまたは再試行対象になる
- 既存invalid final ZIPを削除後に新ZIP作成が失敗してもrawは保持されるが、旧invalid ZIPは残らない
- shutdown時に未完了archiveは次回起動recoveryへ委ねる

## FDR／extensionレビュー

- PC1 core headerはbyte-identical
- header／data column数と重複名を全channelで検査
- extension channel／field／frequency／string lengthに上限
- provider＋channel hashをファイル名へ付加し衝突回避
- header正規化後の重複、`utc`／`ut`予約名を拒否
- provider supplied arrayを登録／publish境界でcopy
- channel失敗を個別隔離
- folder名はmillisecond、ordinal、vessel hash、80文字制限で衝突／path肥大を抑止

50 Hz BANK診断等はcapture arrayをMain Threadで生成するため、0 B/frameは未達です。schema互換とMain Thread disk I/O除去を優先した段階です。

## GPUレビュー

GPU backendは能力検出、device表示、optional shader登録、frame-cost記録、disable／CPU fallbackを持ちます。安全判断、Runway最終認証、control lawへGPU依存はありません。

本ソースにはdispatch対象Compute Shader、GPU buffer、RenderTexture、AsyncGPUReadback実装がないため、GPU accelerationの実効性能はありません。GPU欄が`COMPUTE CAPABLE`でも`Active=false`が正常です。これは明示的な未完了項目です。

## Installerレビュー

旧`rm -rf "$TARGET"`は設定、学習、FlightData、Logsを失うため除去しました。package-owned subtreeのみ更新します。

残留リスクとして、ユーザーが既定directory内を直接改変した場合は更新で置換されます。ユーザー定義は`FlightPlans`／`Airfields`直下の既定外位置へ置く前提です。

## 制御不変条件

- BANK file SHA-256一致
- LAND sourceにFlightCtrlState／throttle／wheel steer／AP arm writeなし
- legacy NAV class／entrypoint／capabilityなし
- MASTER OFF／scene reset／vessel resetはworker／writerを待たない
- invalid sensor sampleはPROTECT／directorをUnavailable／Standbyへ退避
- GPU／Writer／Archive failureはflight-control callbackへ例外を返さない

## 総合残留リスク順位

1. ネイティブコンパイル未実施によるAPI／assembly reference不整合
2. 実KSP thread scheduling／frame P95／queue freshness未測定
3. 実GPU pipeline未実装
4. 0 B/frame未達・未測定
5. disk full／hang／permission failureの実fault injection未実施
6. PC1／LANDの実機受入がこの環境では未確認

以上により、配布状態は完成DLLではなく、再展開検証済みSource RCとします。

