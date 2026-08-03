# AERIS v0.18.0.0 CP3 Gate 1
## Current Body Resident Cache Contracts

## 実装目的

CP2.5最終原本へ、現在天体専用のTerrain payload owner
`AERISCurrentBodyResidentCache`を追加した。

Gate 1は所有権契約の実装だけを行う。SSDからのpayload read、decode worker、
通常viewport、前方回廊、LAND、Render Ready、GPU Readyにはまだ接続しない。
したがって、この版だけではTerrain表示速度は変化しない。

## 状態契約

```text
INDEXED
SSD READY
DECODED
RAM RESIDENT
RENDER READY
GPU READY
```

Gate 1で実際に遷移可能なのは、将来接続用APIとしての
`INDEXED → SSD READY → DECODED → RAM RESIDENT`までである。
`RENDER READY`と`GPU READY`は状態定義だけを固定し、Gate 4まで実経路を持たない。

## 所有権

各非同期処理は開始前に`AERISResidentCommitToken`を取得する。
commit時に次をすべて再検証する。

- tile StableId
- 現在天体名
- 天体半径
- environment hash
- LOD
- Resident Cache scope generation
- body generation
- Terrain Preload database generation

天体遷移、scene reset、database generation変更後の旧tokenは拒否される。
他天体または異なるenvironment hashのtileも拒否される。

## RAM予算・LRU・pin

- 既存hot viewport RAM cacheとは別owner、別カウンタ、別budget
- Gate 1暫定AUTO budgetは既存hot RAM budgetの4倍
- 最小256MiB、最大4GiB
- unpinned payloadはLRU順にevict
- pinはlease方式で、二重Disposeしても安全
- pin済み必須payloadだけは一時的なbudget超過を許容
- scene reset、body transition、shutdownではpinを含め強制失効

Gate 2で実測payload容量を取得した後、専用ユーザー設定を追加するか再判断する。

## Map DRAM分離

`AERISMapDramCache`は変更していない。引き続きmetadata-onlyであり、
compressed payload、decode済み高度配列、mesh、RenderTexture、GPU resourceを保持しない。
Resident CacheはMap DRAMへ書き込まず、同期disk I/Oも行わない。

## ライフサイクル接続

- Flight中の現在天体とdatabase generationを同期
- 固体表面がない天体、またはenvironment fingerprintが空の場合はinactiveへfail-closed
- 40km altitude gateでviewportが停止していてもbody scope契約は維持
- scene resetで全entry・pin・tokenを失効
- shutdownで全entryを破棄

## Gate 1で行わないこと

- main thread同期SSD read
- decode job投入
- preload chunk readへの接続
- existing hot/warm cacheからの読み替え
- ND/Terrain draw pathへの接続
- LAND専用payloadの展開
- RenderTexture、mesh、ComputeBuffer等の生成
- Flight安全laneの使用
- Track B滑走路データ、AA、AP、PROTECT、LANDの変更

## 次のGate

CP3 Gate 2で、Map DRAMのmetadata lookupを起点として、shared worker lane上で
`SSD READY → DECODED → RAM RESIDENT`へ昇格させる。commit tokenが古い場合は
結果を破棄し、main threadでSSDを読まない。
