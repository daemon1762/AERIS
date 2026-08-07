# Operation Health Step 3 — Worker Projection

## 目的

Step 2で成立した固定10 Hzの正確なND表示、Motion / Content分離、Candidate11相当以上の地図品質を維持したまま、移動中のper-vertex投影計算をKSPメインスレッドからGeneralCompute workerへ移す。

## 実装範囲

Worker Projectionは、既存FRONTがREADYでContent snapshotに変更がなく、移動によるexact projection refreshだけが必要な安定飛行時に限定して使用する。

初回ロード、Range/View変更、TerrainGeneration変更、ContentRevision変更、色モード変更、LOADING中は従来のmain-thread exact rendererを使用する。

## Worker契約

Workerへ渡すものは以下の純粋データだけとする。

- immutable `AERISNdMapProjection` snapshot
- `GeographicUnitPoint[]`
- plain `float U[] / V[]` output buffer
- generation / content revision / FRONT swap generation等の照合情報

Workerから禁止するもの：

- `Mesh`
- `Material`
- `RenderTexture`
- `Transform`
- `Vessel`
- `CelestialBody`
- `Graphics` / `GL`
- `Vector3`
- `UnityEngine.Object`

Workerは既存の`ProjectUnitToRenderNUp`と同一のexact projection式を使用する。

## Main Thread Commit

Worker完了後、Main Threadで以下を行う。

1. FRONT swap generation、TerrainGeneration、ViewGeneration、ContentRevisionを再照合
2. Body、色モード、Entry参照を再照合
3. 全Mesh/source/U/V buffer長を事前検証
4. Runway Map Lock error <= 1 pxを再検証
5. U/Vを既存の`Vector3[]`へpack
6. `mesh.vertices`へupload
7. 既存BACK rendererで描画
8. 既存`SwapFrontAndBack`でatomic commit

一つでも不整合があればworker結果は破棄し、古い結果が新FRONTを上書きしない。

## 10 Hz authority

Worker submitが10 Hzでも非同期完了時刻は揺れるため、Worker FRONT commitにも独立して`0.10 s`の最小間隔を設ける。

完了が早すぎる場合は結果を破棄せず保持し、次のRepaintでcommit可能になるまで待つ。これによりWorker completion jitterからsub-100 ms FRONT burstが発生しない。

## Fallback

Workerがpending、schedulerがacceptしない、または他の理由で使用不能な場合は、そのtickだけ従来のmain-thread exact projectionへfallbackする。品質・正確性・10 HzをWorker成功率のために下げない。

## 維持する品質authority

- authoritative ND cadence: fixed 10 Hz
- Candidate11 contour level budget: 96
- sparse coastal parent safety cap: 256
- HD coastline payload: 129 x 129 / format v2
- Step 2 coastal edge refinement
- RenderTexture: ARGB32 / Bilinear
- Hotfix4 LOADING / READY分離
- actual FRONT swap同期のownship/vector/range fan

## Runtime telemetry

- `oh_project_worker_submit`
- `oh_project_worker_commit`
- `oh_project_worker_fallback`
- `oh_project_worker_stale`
- `oh_project_worker_fail`
- `oh_project_worker_vertices`
- `oh_project_worker_defer`
- `project_worker_buffer_bytes`
- `project_worker_ms`
- `project_worker_pending`

## Runtime合格条件

- local KSP-reference xbuild成功
- 80/160 km安定飛行でFRONT swap約9–10 Hz
- sub-100 ms FRONT burstなし
- gettan再発なし
- LOADING/READY正常
- coastline/fill/stair refinementがStep 2品質を維持
- `oh_project_worker_submit`と`commit`が安定飛行中に増加
- fallback/stale/failが異常増加しない
- `oh_project_worker_fail=0`
- AERIS error / exception / GPU terrain failure / forced recovery / ready-build violationなし

## Rollback

Step 2 runtime PASS rollback branch:

`baseline/operation-health-step2-runtime-pass`

baseline commit:

`0b41fe0c5366262e25d761bc1d65bb1afb44c84e`

Step 3はruntime acceptance完了までRelease/Pre-release対象外とする。
