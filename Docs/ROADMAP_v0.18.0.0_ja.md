# AERIS v0.18.0.0 内部ロードマップ

最終v0.18.0.0は、改良型ND、Physical Runway統合、Approach Procedure Registry、節度ある可変グライドパス、障害物対応進入コースを一つのリリースに統合する。途中成果物はDEVチェックポイントであり、完成版・RCではない。

## Gate 0 — Physical Runway Canonical Federation

状態：**ソース実装・静的回帰完了。**

- Provider alias統合
- schema 7 cache migration
- PhysicalRunwayId／signature
- Adaptive Approachのcontrol-free基盤

## Gate 1 — ND計測・レイヤー分離

状態：**CP1完了、ユーザー判断により続行承認済み。**

- symbology 30～60Hzとslow data更新を分離
- Snapshot／generation契約
- ND Layout／Repaint／capture／worker telemetry
- ND専用ThreadPoolなし

## Gate 2 — 滑走路常時表示・選択・PLAN

状態：**CP1完了、ユーザー判断により続行承認済み。**

- runway symbol常時表示
- click preview＋明示SELECT
- TRACK UP／NORTH UP
- PLAN／RECENTER
- 10／20／40／80／160km

## Gate 3 — Terrain Tile／LOD／三層Cache

状態：**CP2 Preload Terrain統合・静的回帰完了、実KSP試験待ち。**

- 全固体天体を対象とするbody-fixed tile
- Preload Terrain Map Builderによる非Flight事前生成
- 起動時はbinary manifest／index確認のみ
- spatial chunk型Preload Terrain Database
- UInt16量子化＋Row predictor＋Deflate／Raw
- Hot RAM／Warm RAM／Cold Disk／VRAM
- Global／Far／Route／Local／LAND LOD
- Terrain Block PipelineはDB missだけを補完
- bounded queue、read lane、I/O arbitration
- atomic commit、CRC、journal、部分復旧
- body／vessel／view／range／plan／terrain／database generationによるstale reject

## Gate 4 — GPU Terrain表示

状態：**CP2 Preload Terrain統合・静的回帰完了、実GPU試験待ち。**

- GPU高さタイル描画
- TOPO／REL色、固定北西陰影、等高線、水面
- 高度変更はuniform更新を優先
- GPU failure時はterrain layerのみdegrade
- runway／traffic／guidance／FDIは継続
- Protect用Terrain Awarenessは独立維持

## Gate 5 — ND操作・品質・アクセシビリティ

状態：**CP2 Preload Terrain統合・静的回帰完了、実KSP試験待ち。**

- AUTO／TOPO／REL／OFF
- ECO／BALANCED／HIGH／ULTRA＋Automatic
- 高負荷時は遠方情報から段階劣化
- STANDARD／RED-GREEN／BLUE-YELLOW／HIGH CONTRAST
- Track Vector／TRAIL／TRAFFIC／Wind Provider
- traffic警報のND・FDI・FDR連携
- 機体署名別ND設定
- LAND profile COMPACT／NORMAL／LARGE

## Gate 6 — Approach Procedure Registry接続

状態：**未着手。CP2実機判定後にCP3として開始。**

- Physical runway directionごとのDIRECT／OFFSET／DOGLEG／STEEP
- stable procedure identity
- terrain／obstacle／generation signature
- ND平面図／縦断profile

## Gate 7 — 可変Glide Profile

状態：未着手。

- 2.5～6.0°の節度ある探索
- 機体性能制約
- V/S、失速、pitch、flare、runway length
- constant-angle finalと連続transition

## Gate 8 — 3D Obstacle Corridor

状態：未着手。

- PQS terrain
- Static／建造物
- corridor幅
- turn radius／bank制約
- missed approach同時成立
- 片方向限定／条件付き／認証不能の分類

## Gate 9 — LAND表示統合

状態：未着手。

- 平面経路
- 縦断profile
- clearance表示
- procedure選択
- Shadow Guidanceのみ

LAND制御権限はまだ与えない。

## Gate 10 — 総合受入

状態：未着手。

- 長時間
- scene／vessel／body遷移
- cache破損復旧
- GPU OFF／failure
- low-worker
- archive／logger
- Stock／KK／SLE／User CFG
- 高地・短距離・片方向・障害物・Go-Around

## その後

独立LAND完成・総合受入後にのみ、新NAVを完全新規開発する。旧NAVコード、fallback、Kramax runtime依存は戻さない。

## 分割チェックポイント

- CP1：Gate 1–2。完了。
- CP2：Gate 3–5。本パッケージ。実KSP検査待ち。
- CP3：Gate 6–8。Approach Registry接続、可変Glide、3D obstacle corridor。
- CP4：Gate 9–10。LAND表示統合、長時間・総合受入、正式v0.18.0.0。

## CP2 Field Render Consistency Hotfix 1 現在地

Terrain Supply Hotfix 3／Preload統合後の実KSPログで、供給自体は進む一方、Range・表示切替時の世代整合、途中Tileの完成判定、Preview／Final合成、実三角形coverageに不具合を確認した。

Field Render Consistency Hotfix 1で次を修正済み。

- 表示モード変更と高さTile世代を分離
- active Block workへ最新generationを統合
- Sampling完了状態を明示
- partial Previewの誤Final昇格を禁止
- Range／style切替中の完成fallbackを維持
- coverageをTile存在ではなくvalid triangle／qualityで算出
- AUTO backlogを評価窓内でlatch
- Range／mode／AUTO遷移をログ化

修正ソースの静的回帰後も実KSP再試験は必須であり、合格まではCP3へ進まない。詳細な現行判定は`ROADMAP_CURRENT_AERIS13_v0.18.0.0_ja.md`を正とする。
