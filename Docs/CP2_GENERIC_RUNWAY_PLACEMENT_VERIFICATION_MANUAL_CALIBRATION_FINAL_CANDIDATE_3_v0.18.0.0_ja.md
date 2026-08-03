# CP2 Generic Runway Placement Verification / Manual Calibration Final Candidate 3

## 1. 目的

Hotfix 3のAnchor Surface Scanは「launch anchorへ接続した一定幅の物理面」を検出できたが、実際に使用すべき滑走路とは別の長方形面を、内部的に整合したまま認証する余地が残った。`AERISFlightControl(18)`のKola Islandがその実例である。

Final Candidate 3では、特定空港の例外リストだけに頼らず、ユーザーが目視確認した実滑走路上の機体位置を独立証拠として、任意の空港で同じ種類の位置ずれを判定できるようにする。

## 2. 汎用実地点照合

AIRFIELDSの滑走路詳細に`CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY`を追加する。

実行条件：

- 選択空港と機体が同一天体
- 機体がPRELAUNCHまたは地上状態
- 着水状態ではない
- 対地速度5m/s以下
- 選択滑走路の両端座標が有限
- 天体半径、滑走路幅、不確かさが非有限の場合は安全な既定値へ正規化

計算：

- 滑走路端A→端Bの大円距離と初期方位
- 端A→機体の距離と方位
- 長手距離 `along = distance × cos(deltaBearing)`
- 横ずれ `cross = distance × sin(deltaBearing)`
- 横回廊 `max(width/2 + 12m, centerlineUncertainty×3 + 12m)`
- 端余裕 `max(100m, width×1.5)`
- 高度余裕 `max(25m, elevationUncertainty×4 + 10m)`

判定：

- 長手範囲外または高度差過大：INCONCLUSIVE。保存・隔離しない。
- 横回廊内：PASS。現在の認証位置と実地点が整合。
- 長手・高度範囲内だが横回廊外：PLACEMENT MISMATCH。永続隔離して二点校正必須。

このメソッドにはKola Islandの名称、UUID、座標を含めない。同じ判定を他のKK/SLE/Stock/DLC滑走路にも使用する。

## 3. 永続隔離と手動救済

配置不一致を検出したproviderについて、`GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg`へ次を保存する。

- provider stable ID / UUID / site ID / source path
- `placementMismatchObserved = True`
- 観測横ずれ、長手距離、許容回廊
- 観測詳細

不一致記録時には古いA/B端点を無効化する。これにより、過去の手動校正が残っていても隔離を迂回できない。

再測量時は、完全な二点校正がない限り`UserCalibrationRequired`でfail-closed。AIRFIELDSで実滑走路の一端を`MARK A`、反対端を`MARK B`として登録し、80m以上の有限な二点が揃った場合のみ`UserCalibrated`として再認証できる。

User Calibration schemaは2。schema 1は読み取り互換を維持し、保存時にschema 2へ移行する。provider UUID／site IDによる照合は天体一致も必須とし、別天体の同名siteへ校正を誤適用しない。

`MARK A/B`も接地・停止中だけ許可する。端点距離不足または保存失敗時は変更前のメモリ状態へ戻し、ディスク保存に失敗した校正・隔離が現在セッションだけ有効になる状態を禁止する。

## 4. Kola Island

`AERISFlightControl(18)`では、認証中心線長2509.93m、幅77.74m、中心線不確かさ0.75mに対し、目視上の実滑走路に停止した機体は認証線から約100.29m横へ離れていた。許容回廊は約50.87mであり、汎用判定ではPLACEMENT MISMATCHとなる。

Kolaは既知の確認済み事例なのでcatalogを`ManualRequired`へ変更する。これは実機安全のための既知事例固定であり、他空港の検出は汎用`CHECK HERE`で行う。

## 5. 一時表示の撤去

CP2中に使用した候補滑走路オーバーレイ、候補専用DB、設定トグル、候補ログ、cache内の候補詳細フィールドは削除する。Provisional Geometryは安全状態としてregistryに残せるが、NDへは描画せず、選択・LAND ARM・cache認証へ入れない。

削除対象の名前を実行コード・設定・UI・旧専用試験から除去し、新しい静的受入で再混入を禁止する。

## 6. Auto Preload

Auto Preload Progressionのロジックは変更しない。CP2 CLOSEには、Final Candidate 3のnative Mono/xbuild成功に加え、現行ビルドで以下が必要。

- Kerbin以外の固体天体へ自動進行
- `[PRELOAD_AUTO] COMPLETE`
- 全固体天体Far完了後の`[PRELOAD_AUTO] PROMOTE`
- 再起動後の完成状態継承

## 7. 権限境界

追加機能は観測、設定保存、再測量要求のみ。FlightCtrlState、MainThrottle、操舵軸、LAND ARMを直接操作しない。旧NAVを復活させず、新NAVへ着手しない。
