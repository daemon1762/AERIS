# CP2.5 Integrated Acceptance Candidate 3

## 目的

Candidate 2までのGate 1～4、空カテゴリUI、AA操舵面イベント寿命修正を維持しつつ、次の運用改善を統合する。

1. Main Menu／Space Center／VAB／SPHでTerrain Preload設定と各天体操作を変更できるようにする。
2. ユーザーが明示的に開始・停止する`PRELOAD BOOST`を追加する。
3. ND／FDIの初期表示を「必要時のみ」に変更し、独立した完全OFFを追加する。
4. SPEEDだけが動作中の通常APでは、FDIを速度専用表示として開き、内部に`VEL ONLY`または`ACC ONLY`を明記する。

## 非Flight Preload UI

AERISはMain Menu起動のpersistent bootstrapを既に持ち、Terrain Preload BuilderもFlight以外で動作している。Candidate 3では、従来の読み取り専用status表示を廃止し、Flight内と同じPreload管理ページを非Flightウィンドウから利用する。

対象場面：

- Main Menu
- Space Center
- VAB
- SPH
- Tracking Stationなどその他の非Flight場面

操作可能項目：

- Preload mode：OFF／MANUAL／IDLE ONLY／BACKGROUND／AGGRESSIVE IDLE
- Speed：BALANCED／FAST／MAXIMUM
- 保存容量、idle開始時間
- 天体別priority／quality／容量
- BUILD／PAUSE／RESUME／CANCEL／VERIFY／REBUILD／DELETE
- 手動PRELOAD BOOST

Flight control、AP、AA、PROTECTの操作はこの画面へ追加しない。

## PRELOAD BOOST

`PRELOAD BOOST`は設定ファイルへ保存せず、KSP起動や場面遷移で自動開始しない。開始は`START PRELOAD BOOST`、通常停止は`STOP PRELOAD BOOST`だけで行う。

有効中は、既にAERISが生成済みのworker poolを最大限使用する。

- 全既存AERIS worker permitをPreloadへ利用可能にする。
- 非Flight中のsafety reserveを0へ変更する。
- archive laneのpauseを解除する。
- worker thread priorityを`Normal`へ一時変更する。
- Preload compute queueをworker数に応じて満たす。
- chunk writeの同時数をworker数に応じて拡大する。
- PQS main-thread budgetを8～24msへ拡大する。

停止後はadaptive permit制御、safety reserve、`BelowNormal` priorityへ戻る。

### 安全境界

- Flightへ入った場合は、手動停止忘れを吸収するため`FLIGHT_SAFETY`で自動解除する。
- Safety/LAND laneは使用しない。
- private threadや無制限busy loopは作らない。
- Terrain payloadをRAM常駐させない。Current-Body Resident CacheはCP3のまま。
- GPU Preload経路は新設しない。現存するCPU/PQS/compression経路だけを最大化する。

## ND／FDI表示ポリシー

新しい選択肢は両表示で独立している。

- `AUTO`：必要な場合だけ表示
- `ALWAYS`：常時表示
- `OFF`：要求があっても完全非表示

factory configと設定resetはAUTO。以前のモデルrevisionからは一度だけ旧ALWAYS既定値をAUTOへ移行し、`displayPolicyRevision = 1`を保存する。

### FDI AUTO需要

次のいずれかで表示する。

- BANK／HDG
- PITCH／V/S／ALT
- ACC／VEL
- Terrain／Traffic／PROTECT警告
- External Automation／instrument provider表示

SPEEDのみが有効で、他の通常AP軸や警告表示がない場合、FDIは次の専用表示になる。

```text
FDI — SPEED GUIDANCE
AP SPEED — VEL ONLY
```

または、

```text
FDI — SPEED GUIDANCE
AP SPEED — ACC ONLY
```

FDIをOFFにした場合は、SPEED動作中でも表示せず、provider収集・detail生成・FDI gauge描画も行わない。

### ND OFF

NDをOFFにした場合、ND panelだけでなくNDが所有するsnapshot、CPU raster、GPU display更新も停止・解放する。ただしTerrain Awarenessの安全状態、Altitude Gate、Preload Builderは独立して継続する。

## Candidate 2から変更しない範囲

- `SyncModuleControlSurface`と`CtrlSurfaceUpdate`
- AP／AA／PROTECT／FlightState
- LAND、Airfield Registry、滑走路座標
- Gate 1～4の中央policy
- Map DRAM metadata-only契約
- Track B滑走路情報
