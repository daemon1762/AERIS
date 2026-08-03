# CP3 Gate 1 Current Body Resident Cache Contracts Compile Hotfix 1

## 発生した不具合

ユーザー環境のMono/xbuildで、`AERISTerrainPreloadBuilder.cs`に残った3か所の
`ApplyStandardSchedulerState(...)`呼出に対し、メソッド定義が存在しないため
`CS0103`が発生した。

## 原因

CP2.5 Final ClosureでFULL BOOST経路を削除した際、STANDARD専用のScheduler状態切替
ラッパーまで同時に削除された。一方、非Flight時のSTANDARD適用、Flight時の解除、
Dispose時の解除という3つの呼出と`standardSchedulerApplied`状態変数は残っていた。

従来の静的検査は呼出の存在を確認していたが、対応する定義の存在を確認していなかった。
このため静的受入は通過したが、実コンパイラで初めて欠落が検出された。

## 修正

`AERISTerrainPreloadBuilder`へ次の契約を持つprivate helperを復元した。

- 同一状態への重複適用を抑止する。
- `AERISPerformanceRuntime.Current`が未生成なら何も変更せず安全に戻る。
- 既存の`AERISWorkerScheduler.SetStandardPreloadThroughput(active)`だけを呼ぶ。
- Scheduler更新成功後にだけ`standardSchedulerApplied`を更新する。

FULL BOOST、同期SSD read、Terrain payload decode、描画、GPU、LAND接続は追加しない。
CP3 Gate 1の`payloadRoute=DISCONNECTED`境界は維持する。

## 回帰防止

Gate 1 runnerの先頭へ専用検査を追加した。以下を必須とする。

- `ApplyStandardSchedulerState(bool)`定義がちょうど1個。
- 呼出3個＋定義1個が存在。
- 既存STANDARD Scheduler APIへ接続。
- runtime未生成時にfail-safe。
- FULL BOOSTおよび飛行制御権限の再導入なし。
