---
artifact: AERISFlightControl-v0.17.0.0_PerformanceRuntime_Source
status: SOURCE_RC
date: 2026-07-23
next_gate: native_build_and_ksp_performance_acceptance
nav_status: BLOCKED_UNTIL_LAND_ACCEPTANCE
bank_source_sha256: bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7
---

# AI向け引継ぎ — v0.17.0.0 Performance Runtime

## 最初に読む結論

これは完成DLLではなく、静的／モデル／再展開検証対象のSource RCである。

次の担当AIは、ソースを不用意に変更する前に:

1. ZIP SHA-256を確認
2. v0.17.0.1では`python3 Tools/run_v01701_acceptance.py`
3. KSP参照DLLを用いて`build_ubuntu.sh`を実行
4. `Docs/PERFORMANCE_TEST_CARD_v0.17.0.1_ja.md`の2-worker／GPU OFFから開始
5. 実測証拠がない性能・安全項目をPASSにしない

## 正式な順序

```text
旧NAV削除 → 独立LAND完成 → 新NAV新規開発
```

旧NAVは削除済み。LANDは現段階で認証／表示／観測専用。LAND総合受入前に新NAVを実装してはならない。

## 入力基準

- Precision Coding #1後のv0.16.4.0成果物を基準にした
- v0.16.3.0 referenceへ巻き戻していない
- PC1 BANK fileをbyte-identicalで保持
- PC1 15 consensus runway casesを保持
- startup/manual reload、Approach Freeze、AIRFIELDS UIを保持

## 実装されたもの

### Performance Runtime

- `Performance/AERISRuntimeGeneration.cs`
- `Performance/AERISWorkerScheduler.cs`
- `Performance/AERISCanonicalSnapshot.cs`
- `Performance/AERISGpuAssistBackend.cs`
- `Performance/AERISPerformanceRuntime.cs`
- `Performance/AERISBackgroundFileWriter.cs`
- `Performance/AERISInstrumentPipeline.cs`

### CPU policy

- AUTO AGGRESSIVE
- logical CPUの約80～85%、最低2 logical threadを予約
- fixed 12-worker capなし
- 2-worker明示構成あり
- frame／AERIS Main／queue delay／writer backlog／GPU timeで自動後退
- 15秒安定でpermitを1ずつ回復

### Async workload

- Runway certification
- Terrain raster
- Instrument display preprocessing
- FlightData recovery scan
- ZIP compression／verification

### Async I/O

- AERISLogger
- CVR
- FDR main
- BANK／HDG／PITCH／V/S／ALT／ACC／VEL／Ground diagnostics
- AA comparison
- extension telemetry
- runtime telemetry

## 絶対に維持する境界

- FlightCtrlState、control ownership、MASTER OFF、PROTECT、final control lawはMain Thread
- WorkerへVessel、CelestialBody、Texture2D、GameObject、UnityEngine.Objectを渡さない
- Worker resultはgeneration一致時だけcommit
- LAND ARM後はselected geometryをdeep freeze
- Reload resultはstaged、DISARMまで交換しない
- GPUを唯一の安全根拠にしない
- Writer／Archiveを待ってownership releaseを遅らせない
- rawはZIP完全検証後だけ削除
- legacy NAVを復元しない

## 最終監査で追加した重要修正

1. 2-worker／GPU OFFをUI／CFGから再現可能にした
2. settings loadをruntime constructionより先にした
3. permit／archive policyをvolatile publishにした
4. snapshot capture costの旧値反復計上を除去
5. snapshot／commit／AERIS Main P95を分離
6. Diagnostics telemetryを1 Hz cacheし毎frame sortを除去
7. failed stream Disposeをproducer queue lock外へ移動
8. Writer percentile sortをproducer queue lock外へ移動
9. Close path保持、Open／Close dropのunsealable化
10. 新規sessionをrecovery scanから明示除外

## 現在の試験結果

最終ツリー:

```json
{
  "scripts_passed": 11,
  "scripts_total": 11,
  "assertions_passed": 536,
  "assertions_total": 536,
  "source_model_assertions": 533,
  "csharp_syntax_files_passed": 108,
  "csharp_syntax_files_total": 108,
  "bash_syntax": "PASS",
  "csproj_xml": "PASS",
  "native_compile": "NOT_RUN",
  "ksp_flight": "NOT_RUN"
}
```

v0.17.0.1の内訳は`ACCEPTANCE_v0.17.0.1.txt`を参照。

## 未完了／誤ってPASSにしてはいけないもの

- KSP assembly referenceを使うネイティブコンパイル
- 実KSPロード
- 2 workers／GPU OFFの実機基本機能
- AUTO AGGRESSIVEの実CPU使用率
- Main Thread P95改善
- 0 B/frame
- long-duration FDR
- disk full／permission denied／hang
- Linux／Windows／各graphics API
- GPU compute dispatch（shader未同梱）
- J-1／HHC-4飛行
- LAND総合受入

## GPUの正確な状態

`AERISGpuAssistBackend`はcapability probe、optional shader registration、disable、CPU fallback、telemetryを実装している。しかし本パッケージはCompute Shader／AssetBundleを同梱せず、dispatchも行わない。

従って:

- `Supported=true`はhardware capabilityのみ
- `Active=false`が通常
- GPU性能改善を主張しない
- CPU deterministic pathが正式

## 0 B/frameの正確な状態

未達／未証明。残る主なallocation:

- FDR captureごとの`AERISCsvField[]`
- 20 Hz instrument prepared arrays
- IMGUI文字列／layout
- 1 Hz percentile snapshot copy

`gc_positive_delta_ema_bytes`はheap-growth proxyであり、allocation profilerではない。

## 実KSPで最初に見る値

`GameData/AERISFlightControl/Logs/Sessions/*_performance_runtime.csv`:

- `configured_workers`
- `active_permits`
- `queue_delay_p95_ms`／`queue_delay_max_ms`
- `aeris_main_p95_ms`
- `snapshot_capture_p95_ms`
- `commit_drain_p95_ms`
- `runway_worker_p95_ms`
- `terrain_worker_p95_ms`
- `writer_*`
- `archive_*`
- `gpu_active`／`gpu_backend`
- `degradation_reason`

## 不合格分類

| 症状 | 第一分類 | 最初に確認するもの |
|---|---|---|
| DLLが読めない | build/API mismatch | KSP.log、assembly refs |
| MASTER OFFが遅い | safety blocker | main thread wait、exception |
| 古い滑走路が出る | generation/freeze | DB／selection revision、stale count |
| Terrainが止まる | worker freshness | queue、terrain result age、generation |
| FDR欠落 | writer backpressure | drop count、first/last seq、failure |
| rawが消えた | archive integrity blocker | ZIP verify log、sourceDeleted |
| frame悪化 | policy/allocation | Main P95、permit、Profiler |
| GPU差で認証差 | safety design blocker | CPU exact path、GPU active |

## 修正へ進む条件

- native compile error: 最小差分でAPI互換を修正し、全受入を再実行
- runtime exception: KSP.logのstack traceとgeneration／queue telemetryを突合
- performance fail: profiler callsiteを特定せず閾値だけ緩めない
- queue overflow: capacityを無条件拡張せず、生成率／coalescing／priorityを先に修正
- flight-control regression: Performance Runtimeを迂回して原因分離し、BANK／LAND不変条件を壊さない

## 再梱包条件

コード／文書を1 byteでも変更した場合:

1. 全acceptance
2. C# 108 files構文解析
3. BANK hash
4. manifest再生成
5. ZIP再生成
6. ZIP SHA-256再生成
7. 別directoryへ再展開
8. 再展開物で全acceptance／manifest／構文解析

旧hashを流用してはならない。
