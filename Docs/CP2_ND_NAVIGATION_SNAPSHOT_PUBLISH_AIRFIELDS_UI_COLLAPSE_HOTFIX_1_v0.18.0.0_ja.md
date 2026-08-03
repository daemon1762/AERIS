# CP2 ND Navigation Snapshot Publish / Airfields UI Collapse Hotfix 1

## 対象

- 基準原本：`AERISFlightControl-v0.18.0.0_DEV_CP2_GenericRunwayPlacementVerification_FinalCandidate3_CompileHotfix1_CalibrationRoundTripHotfix1_BidirectionalRunwayPairHotfix1_Source`
- 入力証拠：`AERISFlightControl(20).zip`、`Video_2026-07-26_18-28-07.mkv`
- CP2状態：OPEN

## 実機で成立していた事項

最新ログでは手動二点校正の保存と相反滑走路生成は成立している。

- `save verified; records=1; fullRoundTrip=True; committedReadback=True; reciprocalPairSchema=3`
- `THRESHOLD B ... reciprocalPair=True`
- `RECIPROCAL PAIR GENERATED ... localizerPair=True; approachValidation=INDEPENDENT`
- 校正後の再測量は`CERTIFIED 13 RWY / 19 APP`へ増加

したがって、今回の主問題は校正登録そのものではない。

## NDから滑走路が消えた根本原因

`AERISNavigationDisplay.CaptureNavigationSnapshot`は、登録済み空港から`runwaySources`と`facilitySources`を構築していたが、生成したデータを`AERISPerformanceRuntime.SubmitNavigationDisplay`へ渡していなかった。

ND描画側はPrepared frameについて次を要求する。

- 現在天体一致
- `DatabaseRevision`一致
- `SelectionRevision`一致

空港の再測量や手動校正によりDatabase Revisionが更新されると、旧Prepared frameは正しくstaleとして拒否される。しかし新Snapshotがworkerへsubmitされないため、新しいPrepared frameが発行されず、ND右上は`NAV DATA`となり滑走路・空港記号が消え続けた。

### 修正

`CaptureNavigationSnapshot`から次を含む`AERISNavigationDisplaySnapshot`を生成して共有Performance Runtimeへsubmitする。

- Runtime Generation Stamp
- Body name / radius
- Capture origin
- Database Revision
- Selection Revision
- Runway sources
- Facility sources

submit成功後にのみcapture済みRevisionと10秒cadenceを更新する。submitが拒否された場合はRevisionをcapture済みにせず、0.5秒後に再試行する。

## AIRFIELDS UI破綻の原因

実機設定ではメインウィンドウが520×920で、CERTIFIED一覧が開いた状態だった。そこへ以下が重なった。

1. 完全な累積Hotfix identityを通常ラベルへ表示していた。
2. 空港行を22px固定・1行ボタンで表示していた。
3. Provider、Basis、Geometry、Threshold、Uncertainty、Calibration messageを折返し無しの長いラベルで表示していた。
4. 旧設定ではCERTIFIED／FAILED／PROVISIONALが開いた状態を初期値としていた。

### 修正

- メインUIのタイトルは`AERIS v0.18.0.0 DEV CP2`へ短縮。完全identityはログとBuild metadataに保持。
- AIRFIELDS専用のword-wrap label/button styleを追加。
- 滑走路行は2行構成、38px高とする。
- CHECK HEREボタンは折返し可能な36px高とする。
- AIRFIELDSの全カテゴリを閉じた状態を初期値とする。
- `airfieldsUiLayoutRevision = 1`による一度限りの既存設定migrationを実施し、旧設定の開いた一覧も初回起動時に閉じる。
- 水平スクロールは無効、縦スクロールのみを使用する。

## 安全境界

変更対象はND用表示Snapshot供給とSYSTEM > AIRFIELDSの表示状態のみ。

以下は変更しない。

- 滑走路認識・認証アルゴリズム1710
- 手動二点校正schema 3
- 相反2方向生成
- LANDの観測専用境界
- AP、AA、APP、FlightCtrlState、MainThrottle、ブレーキ、操舵
- Auto Preload Progression
- 旧NAV削除状態

## CP2 CLOSE条件への影響

このHotfixはCP2 CLOSE候補ではあるが、native Mono/xbuildとKSP実機で以下を再確認するまでCP2を閉じない。

- 空港再測量・校正後もND runway countが復帰する。
- `NAV DATA`固定にならない。
- 全AIRFIELDSカテゴリが初回は閉じている。
- カテゴリを開いた際に文字が重ならず、縦スクロールで閲覧できる。
- Kolaの手動校正pairが再起動後も維持される。
- Auto Preload Progressionの残存受入条件を満たす。
