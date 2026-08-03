# CP3 Gate 2 — Decode／RAM Resident

## 原本

`AERISFlightControl-v0.18.0.0_DEV_CP3_Gate1_CurrentBodyResidentCacheContracts_CompileHotfix1_Source`

Gate 1のネイティブビルドおよびKSP実機動作PASSを受け、Current Body Resident Cacheへ最初の実payload経路を接続する。

## 実装範囲

- 現在天体のPreload Terrain Database登録済みpayloadだけを対象とする。
- Map DRAM Cacheはmetadata-onlyのまま維持する。
- SSD payload read／decodeは既存`AERISWorkerScheduler`の`GeneralCompute` laneで非同期実行する。
- `INDEXED → SSD READY → DECODED → RAM RESIDENT`をgeneration token付きで実行する。
- 通常viewport要求がResident Cacheに存在する場合、SSD read／decodeを行わず共有immutable payloadを再利用する。
- 現在天体の登録済みtileを`Global → Far → Route → Local`の順でbackground populationする。
- background populationは同時に最大1 chunkとし、通常viewport readが先にscheduler枠を取得する。
- RAM不足時は`Local → Route → Far → Global`の順で縮退する。低優先度tileは高優先度foundationを追い出せない。
- body、environment、database request epoch、sceneが変化した場合、旧tokenのcommitを拒否して旧天体payloadを破棄する。
- databaseのappend-only manifest generation更新は既存payloadを失効させず、破損修復・削除等で`RequestGeneration`が変わった場合だけscopeを更新する。

## Gate 2で接続しないもの

- LAND LODのResident population
- 速度予測型Forward Corridor
- LAND／Runway pin policy
- RENDER READY
- GPU READY
- GPU resourceの常駐
- 他天体payloadのRAM常駐
- FULL BOOST

## UI／telemetry

タブ上部のbuild表記を次へ更新する。

```text
AERIS v0.18.0.0 DEV CP3 GATE 2 — DECODE / RAM RESIDENT
```

SYSTEMのperformance表示へ次を追加する。

- Current Body Resident Cache使用量／budget
- Global／Far／Route／Local resident数
- async decode成功数／投入数／失敗数
- 現在status

## 安全境界

- main thread同期SSD readを追加しない。
- `SafetyLand` laneを使用しない。
- AA／AP／PROTECT／LAND制御へ書き込まない。
- Track B滑走路データを変更しない。
- Map DRAMへ圧縮payload、decode済み配列、mesh、RenderTexture、GPU objectを格納しない。
