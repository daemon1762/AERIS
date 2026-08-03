# CP3 Gate 3 — Predictive Forward Corridor

## 原本

`AERISFlightControl-v0.18.0.0_DEV_CP3_Gate2_Decode_RamResident_Source`

Gate 2のネイティブビルドおよびKSP実機動作PASSを受け、現在天体Resident Cacheへ速度予測型の前方回廊要求を接続する。

## 実装範囲

- 対地速度は`vessel.srf_velocity`から天体鉛直成分を除外した水平速度を使用する。
- 進行方向は既存`AERISTerrainAwareness.ResolveMapHeading`へ統一する。
- 旋回率は`vessel.angularVelocity`の天体鉛直軸成分から算出する。
- 予測は一定旋回率モデルを使用し、30～420秒先、最大250kmまでを5段階で投影する。
- 長時間予測で円周を反復しないよう、1計画当たりの予測方位変化を最大135度へ制限する。
- 中心線と左右の不確実性帯を生成し、近距離はLocal／現在near LOD、中距離はRoute、遠距離はFarを要求する。
- 回廊は最大18点、既存Terrain request上限の範囲内でのみ要求する。
- viewport、選択LAND滑走路、前方回廊、backgroundの順序を既存laneで維持する。
- 中心線だけを`ForwardCorridor`理由でpinし、左右端はprefetchとして固定しない。
- viewport中心、Global foundation、選択滑走路端点を理由別leaseでpinする。
- pinはND OFF、高度ゲート、scene／body transition、Reset、Shutdownで解放する。

## LAND／Runway demand

- LAND LODのResident昇格は`landing.Armed`かつLAND detail demand有効中の選択滑走路端点だけに限定する。
- 滑走路の両端についてLAND LODとLocal fallbackを先に要求する。
- LAND LODはbackground population planへ含めない。
- DISARMまたは巡航中はLAND LODを新規要求・常時展開・pinしない。
- LAND pin解放後のLAND payloadは最優先eviction候補とし、Global/Far/Route/Local foundationを圧迫しない。

## Resident Cache

- Gate 2の`INDEXED → SSD READY → DECODED → RAM RESIDENT`をそのまま使用する。
- LAND payloadも同じgeneration token、current-body、environment hash、database request epoch検証を通す。
- Map DRAM Cacheはmetadata-onlyのまま維持する。
- SSD read／decodeは引き続き`GeneralCompute` laneのみを使用し、`SafetyLand` laneは占有しない。

## UI／telemetry

タブ上部のbuild表記：

```text
AERIS v0.18.0.0 DEV CP3 GATE 3 — PREDICTIVE FORWARD CORRIDOR
```

SYSTEM表示へ以下を追加する。

- G／F／R／L／LD resident数
- active pin数
- corridor状態
- 対地速度、旋回率
- 予測距離／時間
- corridor request／pin数
- LAND demand状態

## Gate 3で接続しないもの

- RENDER READY
- GPU READY
- GPU resource常駐
- 他天体payloadのRAM常駐
- NAV／LAND制御出力
- FULL BOOST
