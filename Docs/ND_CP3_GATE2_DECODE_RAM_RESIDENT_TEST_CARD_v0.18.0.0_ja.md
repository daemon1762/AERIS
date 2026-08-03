# CP3 Gate 2 Decode／RAM Resident 実機試験カード

## 1. ビルド

Mono/xbuildでエラーなく完了すること。

ゲーム内タブ上部に次が表示されること。

```text
AERIS v0.18.0.0 DEV CP3 GATE 2 — DECODE / RAM RESIDENT
```

`DEV CP2`または`CP3 GATE 1`が現在build表記として残っていればFAIL。

## 2. Kerbin Resident population

1. Preload Terrain DatabaseにKerbin tileが存在する状態でFlightへ入る。
2. SYSTEMのperformance表示を開く。
3. `CP3 Resident`のbodyが`Kerbin`になること。
4. decode成功数とRAM使用量が増加すること。
5. G/F/R/Lのいずれかが0より大きくなること。
6. KSP.logまたはAERISFlightControl.logに次があること。

```text
[CP3_RESIDENT]
payloadRoute=ASYNC_DECODE_RAM_RESIDENT
```

## 3. Resident hit

同じ領域を離れて戻り、`CURRENT BODY RAM RESIDENT HIT`またはResident hit増加を確認する。再表示時に毎回SSD decode待ちへ戻る場合はFAIL。

## 4. Altitude Gate／ND OFF

海抜40.5km以上へ上昇してND Terrainが停止しても、RAM Resident payloadが即時全消去されないこと。statusに`RESIDENT POPULATION CONTINUES`が現れてよい。

Gate 2ではGPU READYを実装していないため、GPU常駐増加を合格条件にしない。

## 5. body transition

KerbinからMun等の別固体天体へ遷移する。

- active bodyが新天体へ変化する。
- Kerbinのresident数／RAM所有権が新scopeへ持ち越されない。
- stale worker commitで旧天体tileが復活しない。

Jool等の固体表面を持たない天体ではResident Cacheがinactiveになること。

## 6. LAND境界

LAND ARMなしの通常巡航でLAND LOD payloadをpopulationしないこと。Gate 2 UIはG/F/R/Lのみを表示し、LAND promotionはGate 3まで未実装とする。

## 7. 回帰

- ND Terrain表示
- 滑走路／空港表示
- Preload STANDARD
- AA／AP／PROTECT
- LAND ARM／DISARM
- scene transition

上記にGate 1以前からの退行がないこと。
