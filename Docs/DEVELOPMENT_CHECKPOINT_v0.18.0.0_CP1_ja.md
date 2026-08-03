# AERIS v0.18.0.0 DEV CP1 開発チェックポイント

## 位置づけ

本パッケージは、v0.18.0.0の分割開発チェックポイント **CP1（Gate 1–2）** です。完成版、RC、LAND完成版、新NAV実装版ではありません。

CP1の目的は、改良型NDの表示基盤を既存Performance Runtimeへ正式に接続し、地形Tile/GPU処理を入れる前に、表示更新、滑走路常時表示、選択、PLAN操作を独立して実機検証できる状態へ固定することです。

## 実装内容

### ND更新レイヤー分離

- ownship／symbologyは既存の30–60Hz適応更新を継承
- runway／facility snapshotは低頻度のimmutable captureへ分離
- 緯度経度からND用local E/N座標への変換、距離・方位・並べ替えは共有`AERISWorkerScheduler`の`GeneralCompute` laneで実行
- ND専用ThreadPool、`Task.Run`、新規worker基盤は不使用
- generation／latest-wins／stale rejectを既存Runtime契約へ統合
- scene、vessel、body、runway database、selection、layout revision変更時に旧結果を破棄

### 計測

Performance Runtime CSVへ以下を追加しました。

- ND Layout EMA
- ND Repaint EMA
- ND main-thread capture EMA
- ND texture upload EMA
- ND positive GC delta EMA
- captured facility/runway数
- PLAN状態
- range
- ND worker P95
- ND result age

SYSTEM > OPTIONSにもLayout/Repaint/PQSのEMAを表示します。

### 固定レンジ・Orientation

レンジは次の6段階のみです。

```text
5 / 10 / 20 / 40 / 80 / 160 km
```

- TRACK UP：ownshipは画面下寄り
- NORTH UP：ownshipは中央
- 旧automatic-range設定は互換読込みだけ残し、値は最寄りの固定レンジへ正規化

### PLAN／RECENTER

- mapをドラッグするとPLANへ移行
- PLANは常にNORTH UP
- ownship追従を停止し、任意地点を中心に移動
- `CENTER`で選択候補滑走路を中心化
- `RECENTER`でownshipへ戻り、PLAN進入前のTRACK/NORTH設定を復元
- CP1では誤った地形位置合わせを避けるため、PLAN中の地形panは表示しません。Terrain Tile対応はCP2です。

### 滑走路常時表示

- LAND／NAV／AP状態に依存せず、同一天体の登録済み滑走路形状を常時表示
- runway body、centerline、threshold tick、方向名、施設名を距離に応じて表示
- certified／uncertified／selectedを区別
- runway数には表示上限を設けない
- LAND中は非滑走路施設を抑制しますが、全runway layerは維持
- 選択滑走路が画面外の場合はedge pointerを表示

### クリック選択

- runwayを直接クリックしてもRegistry selectionは変更しない
- クリックはPreviewだけ
- `SELECT`でairfieldとdirectionを明示確定
- `CENTER`でPLAN中心化
- 確定済み滑走路に限り`ARM OBS`を許可
- `ARM OBS`は既存のLAND観測ARMであり、FlightCtrlState／AP／throttleへ権限を持ちません

## 維持した安全境界

```text
AP/BANK             byte-identical・凍結
旧NAV               不在
新NAV               完全未実装・LAND完成までBLOCKED
LAND                観測・計画・表示のみ
ND pipeline         FlightCtrlState/KSP/Unity objectをworkerへ渡さない
Safety/LAND lane    ND処理では使用しない
```

## 最終監査で追加した品質修正

- Layout／Repaint以外のOnGUI eventではStopwatch／GC probeを起動しない
- runway database revisionまたはselection revisionが変わった直後は、旧prepared frameを描画しない
- PLAN drag量をND座標変換の厳密な逆倍率へ修正し、従来の2倍速panを排除

## CP2へ持ち越すもの

- PLAN中の正しいTerrain Tile pan
- 全固体天体Tile identity
- Global/Far/Route/Local/LAND LOD
- VRAM/RAM/Disk三層cache
- GPU色付け、陰影、等高線、REL
- GPU failure時terrain layerのみdegrade
- AUTO/TOPO/REL/OFF
- ECO/BALANCED/HIGH/ULTRAの完成
- Track Vector/TRAIL/TRAFFIC/Wind Provider

## 実証状態

このソース作業環境にはMono/xbuild、KSP/Unity参照DLL、実KSPがないため、ネイティブコンパイルと実描画は未実施です。静的受入と独立再展開受入を通した後、ユーザー実機試験を依頼します。
