# CP3開始 — Current Body Resident Cache

## 原本

CP3は`CP2.5 FINAL CLOSURE STANDARD PRELOAD ONLY CP3 ENTRY BASELINE`を唯一の実装原本とする。CP2.5のFULL BOOST系コードを復活させない。

## 目的

現在天体だけのTerrain payloadをRAMへ常駐させ、通常viewport／前方回廊／LAND要求のSSD decode待ちを削減する。Map DRAM Cacheはmetadata-onlyのまま維持し、Resident Cacheとは別owner・別budget・別telemetryで管理する。

## 状態モデル

```text
INDEXED
SSD READY
DECODED
RAM RESIDENT
RENDER READY
GPU READY
```

各tileは世代、天体、LOD、environment hash、database generationで所有権を固定する。古い世代のcommitは破棄する。

## CP3 Gate案

### Gate 1 — Resident Cache Contracts

- `AERISCurrentBodyResidentCache` owner追加
- RAM budget、LRU、pin、generation、body transition契約
- payloadは現在天体だけ
- Map DRAMとの責務分離
- まだ描画経路へ接続しない

### Gate 2 — Decode／RAM Resident

- SSD READYからDECODED／RAM RESIDENTへの非同期昇格
- Global／Far／Route／Localの優先順位
- 容量不足時の段階的縮退
- current body変更時の安全demote／evict

### Gate 3 — Predictive Forward Corridor

- 対地速度、進行方向、旋回率から前方回廊を予測
- viewport、LAND、Runway要求を優先
- cruiseではLAND専用payloadを展開しない

### Gate 4 — Render Ready／GPU Ready

- CPU render-readyデータの常駐
- GPU resourceは必要時だけ昇格
- Altitude Gate OFF、ND OFF、scene transitionでGPU resourceを解放
- compute kernel assetがない場合はCPU authoritative

### Gate 5 — 統合受入

- body transition
- scene transition
- 40km altitude hysteresis
- ND AUTO／OFF
- LAND ARM／DISARM
- 長時間RAM・VRAM・SSD telemetry
- CP2.5回帰

## 禁止事項

- 他天体payloadの常時RAM常駐
- FULL BOOST復活
- Map DRAM ownerへdecode済みpayloadを混入
- main thread同期SSD read
- Flight安全laneの占有
- Track B滑走路データ変更
