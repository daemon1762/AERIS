# CP2.5 Integrated Acceptance Candidate 2

## 目的

Candidate 1のGate 1～4総合合格状態を維持したまま、`SyncModuleControlSurface`がKSPのグローバル`GameEvents`へ残していたcallback所有権を修正する候補版である。

Candidate 1のKSPログでは、破棄済み`AERISFlightControl:SyncModuleControlSurface`から次のcallbackが合計23件検出された。

- `onVesselReferenceTransformSwitch`：20件
- `onEditorPartEvent`：3件

KSPCommunityFixes導入環境では場面遷移時に除去されていたが、AERIS自身が破棄時に解放する。

## 修正方式

`ModuleControlSurface.OnDestroy`はnon-virtualであるため、派生クラス側でUnity messageとして`public new void OnDestroy()`を明示する。

処理順序は次のとおり。

1. stock private callbackを明示的にRemoveする。
2. `base.OnDestroy()`を必ず1回実行する。
3. `finally`で同じRemoveを再実行し、stock側の重複登録や解除漏れを吸収する。

対象callback：

- `OnEditorPartEvent`
- `OnPartActionUIShown`
- `OnPartActionUIDismiss`
- `onVariantApplied`
- `onVesselReferenceTransformSwitch`

`OnStart`の直前にも同じidempotent cleanupを行い、同一moduleに再度Startが来た場合の重複登録を防ぐ。

## 安全境界

- `CtrlSurfaceUpdate`はCandidate 1とSHA-256一致。
- 操舵面のdeflection、actuator速度、pitch/roll/yaw合成、mirror deploy処理は変更しない。
- Harmonyを追加しない。
- AERIS以外のstock `ModuleControlSurface`や他MODの操舵面はpatchしない。
- AP、AA制御則、PROTECT、FlightState、LAND、Terrain、Map DRAM、滑走路データは変更しない。

## 実機合格条件

- 起動後、最初の対象操舵面破棄で次が1回記録される。

```text
[AA/CONTROL_SURFACE_LIFECYCLE] explicit stock callback cleanup active.
```

- Flight／Editor／Space Center／Main Menuを複数回遷移しても、KSPCFログに次が1件も出ない。

```text
callback owned by a destroyed AERISFlightControl:SyncModuleControlSurface instance
```

- BANK／HDG／PITCH／AA FBWの操縦品質に退行がない。
- Candidate 1の空カテゴリUI、Gate 1高度、Gate 3 LAND、Gate 4 Map DRAMをsmoke testして退行がない。
