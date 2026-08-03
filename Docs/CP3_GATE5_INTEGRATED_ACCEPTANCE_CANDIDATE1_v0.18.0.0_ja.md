# CP3 Gate 5 — Integrated Acceptance Candidate 1

## 目的

Gate 4C Compile Hotfix 1で実機合格したproduction runtimeを変更せず、CP3全体を統合受入する。
Gate 5では新機能を追加しない。最終試験で不具合が出た場合だけ、その原因Gateへ戻して修正する。

## 必須試験

### 1. 起動・初期状態
- Main Menu → Space Center → Flight。
- 起動直後は `airport=NONE / runway=NONE`。
- ERROR / Exceptionが発生しない。

### 2. ND表示・Terrain表示
飛行中に以下を順番に確認する。
- ND display: AUTO → OFF → AUTO。
- Terrain: AUTO → TOPO → REL → OFF → AUTO。
- Orientation: TRACK UP / NORTH UP の両方。
- Range: 5 / 10 / 20 / 40 / 80 / 160 kmを各10秒以上。
- 5/10/20 kmでは滑走路端番号、40 km以上では端番号非表示。
- 黒欠け、クソコラ化、地理位置破綻、滑走路Map Lockずれがない。

### 3. 高速・旋回
- 250〜350 m/s程度で連続飛行。
- TRACK UPで360度以上の連続旋回。
- `ready_build_violation=0`、`cpu_terrain_draw=0`。

### 4. 40 km Altitude Gate hysteresis
- 39.5 km未満から上昇。
- 40.5 km以上でTerrain viewport OFFを確認。
- 39.5〜40.5 kmへ降下してもOFFを保持すること。
- 39.5 km未満で再度ONになること。

### 5. LAND
- 空港・滑走路を明示選択。
- LAND ARM。
- 数秒以上観測後DISARM。
- LAND解除後に通常FAR/Virtual Detailへ戻ること。

### 6. Scene transition
- Flight → Space Center。
- Resident RAMが0へ解放されること。
- GPU Terrain表示resource解放はソース契約とscene遷移後の表示停止で確認する。Performance CSVの`terrain_gpu_*`は最終publish値がstaleで残り得るため、scene外で0になること自体は数値FAIL条件にしない。
- 可能なら再度Flightへ入り、再構築できること。

### 7. Body transition
- 別天体に存在する機体へ切替または別天体Flightをロードし、CP3 telemetryで2つ以上の非空body名を記録する。
- 旧天体payloadをcurrent-body Resident Cacheに保持し続けないこと。
- body transition後もdecode/GPU failureがないこと。

### 8. Soak
- Active Flight合計30分以上を最低合格ラインとする。
- 60分以上をExtended acceptanceとする。
- RAM budget超過なし。
- GPU failure / DB CRC failure / hash mismatch / decompress failure / writer failureなし。
- main-thread synchronous SSD read = 0。

## 自動解析

```bash
python3 Tools/analyze_v01800_cp3_gate5_runtime.py \
  /path/to/AERISFlightControl.zip
```

最終判定は `OVERALL: PASS` が必要。
試験操作が不足している場合はFAILではなく `NOT OBSERVED` と表示される項目もあるが、CP3 closureでは必須項目すべてを観測すること。
