# CP2.5 Integrated Acceptance Candidate 4

## 目的

Candidate 3で実装した非Flight Preload管理UIと表示需要ポリシーを維持しながら、実測でCPU・SSD使用率が約2%に留まったPreload処理を、実際に仕事を供給できる多段パイプラインへ再構成する。

今回の中心は次の二点である。

1. Candidate 3の旧BOOST相当を通常Preloadの標準性能へ昇格する。
2. 手動`FULL BOOST`ではPQS生成、CPU圧縮、SSD commitの各段を深く並列・batch化する。

## 標準Preload

非Flight中の標準Preloadは、Candidate 3の旧BOOST相当を使用する。

- 通常worker ceiling内の全permit
- 非Flight safety reserve 0
- worker priority `Normal`
- worker数連動queue
- PQS予算8～24ms
- 最大4,096 samples／update
- parallel encodeおよびSSD super-batch経路を共用

Preload mode、PAUSE、CANCEL、天体別設定など通常の運用条件は維持する。

## 手動FULL BOOST

`START PRELOAD BOOST — FULL`でのみ開始し、`STOP PRELOAD BOOST — FULL`で停止する。設定ファイルには保存せず、再起動時は必ず停止状態となる。Flightへ入る場合は`FLIGHT_SAFETY`で先に解除する。

### CPU／producer

- AERIS scheduler poolを論理CPU数－1まで生成・利用可能にする。
- active permitをFULL ceilingまで開く。
- worker priorityを`AboveNormal`へ一時昇格する。
- GeneralCompute queueを最大512へ、Archive queueを最大128へ拡張する。
- active terrain tile上限を256、tileごとのpending block上限を12へ拡張する。
- PQS予算を48～160ms、最大65,536 samples／update、10,000,000 queries/sへ拡張する。
- final height tileのcodec処理をGeneralCompute worker上で並列実行する。

PQS呼出しそのものはKSP/Unityのmain-thread制約を守る。無制限loopやprivate thread poolは作らない。

### SSD

- 圧縮済みtileをchunk別に集約する。
- 標準は最大32 chunk、FULLは最大64 chunkを一つのsuper-batchとしてcommitする。
- 一つのsuper-batchにつきmanifest更新は一度だけ行う。
- 小さなtileごとのmanifest書換えとwriter starvationを削減する。
- Flight viewportの読込laneとSafety/LAND laneは変更しない。

## GPUについて

Candidate 4はGPU使用率を見せるための空演算を行わない。

KSP実行環境は、このソースZIPに追加した`.compute`ソースをその場でコンパイルできない。実用的なGPU Preloadには、対応Unity版で事前ビルドしたcompute shader asset bundle、`AsyncGPUReadback`、CPU結果照合、fallbackが必要となる。

そのため今回の表示は、compute shader対応GPUでも次のように正直に状態を示す。

```text
GPU COMPUTE CAPABLE — NO PRELOAD KERNEL ASSET / CPU AUTHORITATIVE
```

GPU backendは未実装であり、CPU・SSD throughput改善をCandidate 4の合格対象とする。

## 律速診断

Preload UIへ次を表示する。

- STANDARD／FULL状態
- active pipeline／上限
- pending block上限
- encode queue／active encode
- SSD chunk queue／active writer
- last super-batch chunks／tiles
- SSD write throughput
- `BOTTLENECK`
- GPU stage

主な律速表示：

```text
NO WORK
PQS PRODUCER
CPU COMPRESSION
SSD WRITE
PIPELINE BALANCED
```

## 変更しない範囲

- AP／AA／PROTECT／FlightState
- `SyncModuleControlSurface`と操舵則
- LAND、Airfield Registry、滑走路座標、Track B
- ND／FDI表示需要ポリシー
- Gate 1～4中央Policy
- Map DRAM metadata-only契約
- CP3 Current-Body Resident Cache
