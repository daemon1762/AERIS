# CP2.5 総合受入テストカード — Candidate 3

## 0. ビルド

- SHA-256一致。
- Mono/xbuild成功。
- 起動版名に`CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 3`。
- AERIS起因ERROR／FATAL／Exceptionなし。

## 1. Main Menu／Hangar Preload UI

1. Main Menuで右上の`AERIS PRELOAD`またはtoolbarからPreload画面を開く。
2. PRELOAD mode、speed、storageを変更する。
3. Space Centerへ移動し、設定値が維持されることを確認する。
4. SPHとVABで同じ画面を開く。
5. 任意天体のpriority／qualityを変更し、BUILD→PAUSE→RESUMEを実行する。
6. Flight control UIが表示されず、AP／AAへ書き込まないことを確認する。

## 2. PRELOAD BOOST手動操作

1. 非Flight場面で`START PRELOAD BOOST`を押す。
2. 表示が`BOOST ACTIVE`となり、workers／permits／queue／PQS budgetが0より大きいことを確認する。
3. Preload進捗とqueueが進むことを確認する。
4. `STOP PRELOAD BOOST`を押し、STANDBYへ戻ることを確認する。
5. もう一度開始し、そのままFlightへ入る。
6. Flightでboostが解除され、ログに次が出ることを確認する。

```text
[PRELOAD_BOOST] state=STOPPED; reason=FLIGHT_SAFETY
```

7. KSPを再起動し、boostが自動再開しないことを確認する。

通常の期待ログ：

```text
[PRELOAD_BOOST] state=ACTIVE; trigger=MANUAL; persistence=NONE
[PRELOAD_BOOST] state=STOPPED; reason=MANUAL
```

## 3. ND／FDI初期値と完全OFF

旧Candidate 2設定を保持した状態で初回起動し、次を確認する。

```text
[DISPLAY_POLICY_MIGRATION] revision=0->1; ND=Automatic; FDI=Automatic
```

OPTIONSではND／FDIがそれぞれ`AUTO / ALWAYS / OFF`の3択。

- 地上・APなしではAUTO FDIを表示しない。
- 空中ではAUTO NDを必要時だけ表示する。
- ND OFFではLAND ARMやTerrain需要があってもNDを表示しない。
- FDI OFFではAP SPEED、警告、provider需要があってもFDIとgaugeを表示しない。
- 再起動後も選択値を保持する。

## 4. SPEED専用FDI

1. FDIをAUTOにする。
2. ACCだけをARMする。
3. FDI titleが`FDI — SPEED GUIDANCE`、内部が`AP SPEED — ACC ONLY`になることを確認する。
4. ACCを解除してVELだけをARMし、`AP SPEED — VEL ONLY`になることを確認する。
5. BANKまたはALTを追加すると通常の`FDI — FLIGHT GUIDANCE`へ戻ることを確認する。
6. FDI OFFではACC／VEL中も表示されないことを確認する。

## 5. Candidate 2寿命smoke test

操舵面付き機体でFlight→Space Center/Main Menu→Flightを3往復する。

- `[AA/CONTROL_SURFACE_LIFECYCLE] explicit stock callback cleanup active.`を確認する。
- KSPCFの破棄済み`AERISFlightControl:SyncModuleControlSurface` callbackは0件。
- 最初のFlightで操舵面が通常動作し、逆転・残留がない。

## 6. Gate 1～4 smoke test

- Gate 1：40.5km以上OFF、39.5km未満ON。
- Gate 2：Terrain qualityはAUTO／LOW／MEDIUM／HIGH。
- Gate 3：LAND ARMでACTIVE、DISARMでSTANDBY。
- Gate 4：`SYNC SSD 0 — PASS`、終了時`result=PASS`。

## 提出物

- `AERISFlightControl.log`
- 最新session log／performance CSV
- `KSP.log`
- Main Menu、SPH/VAB、Boost、SPEED-only FDI、OFF動作が分かる動画
