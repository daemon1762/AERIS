# AERIS v0.18.0.0 DEV Gate 0 開発チェックポイント

## 位置づけ

本パッケージは、v0.18.0.0「Integrated ND & Adaptive Approach Registry」の最初の内部ゲートです。**完成版、Release Candidate、実KSP受入済み版ではありません。**

Gate 0の目的は、PSystem、Stock Launchsites Expansion、Kerbal Konstructs、ユーザーCFGなどが同一滑走路を別Providerとして公開しても、AERIS内部では一つの物理滑走路として扱う基盤を確立することです。併せて、将来の改良型ND／LAND表示へ接続する、制御権限を持たない適応型進入方式レジストリの純CPU基盤を追加します。

## 今回実装したもの

### Physical Runway Canonical Federation

- Raw Provider recordをBody、正規化Site、滑走路番号ペア、滑走路軸、位置で保守的にクラスタリング
- 09／27のような相反方向を一つのrunway pairへ正規化
- 09／10など異なるrunway pairを統合しない
- complete-link clusteringを採用し、A–B／B–Cだけが近い場合にA–B–Cを推移的に誤統合しない
- source-authored metadataをcanonical authorityの主基準とし、live Unity Geometryは弱いtie-breakerに限定
- canonical recordへ最良のruntime geometryだけを合成し、source identityとruntime capture availabilityを分離
- 全Provider aliasを監査用に保持
- 通常のPhysicalRunwayIdから可変なalias member集合を除外
- 同一Body／Site／Pair／Axisに複数滑走路がある例外時のみ、粗い位置セルとstable source anchorで分離

### Cache schema 7

- StableRecordIdを`Body + PHYSICAL_RUNWAY + PhysicalRunwayId`へ移行
- schema 6／5／4／3を読込み可能
- Provider別certified／failure aliasを一つのphysical cache authorityへ圧縮
- positive certificationは同じphysical clusterの古いnegative hintを置換
- embedded airfield／runway／direction StableIdもcanonical physical IDへ正規化
- Base64 UTF-8保存、完全往復検証、保存失敗時の旧cache保持を継承

### Adaptive Approach Registry Foundation

- DIRECT／STEEP DIRECT／LEFT・RIGHT DOGLEG候補モデル
- 通常2.5～4.0°、障害物対応4.0～5.0°、対応機のみ5.0～6.0°
- 最終Localizerは常に滑走路中心線と一致
- 障害物回避はmandatory straight finalより外側だけで許可
- Outer Descent／Transition／Stabilized Final／Flare Gateの分節profile
- terrain／obstacle corridor未完成時はPENDING
- missed-approach corridorが不成立ならREJECTED
- body radiusをsnapshotから受け、Kerbin以外でもgeodesic offsetを生成可能
- Registryから外部へ返すprocedureはclone-only snapshot

このApproach RegistryはまだRuntimeへ接続していません。飛行制御、AP指令、LAND自動操縦、NAV経路生成は行いません。

## 変更していないもの

- AP／BANK制御則：凍結、基準ファイルとbyte-identical
- LAND：観測・登録・計画・表示のみ。FlightCtrlStateへの書込みなし
- NAV：完全未実装。旧NAV、fallback、Kramax runtime依存は不在
- Performance Runtime／非同期Writer／FlightData Archive：既存回帰を維持

## 静的受入

最終ツリーでは、manifestを含めた独立再展開受入を実行します。対象は以下です。

- Physical runway federation
- schema 7 alias migration／完全往復
- canonical source geometry cache
- sub-metre compatibility
- adaptive approach safety model
- generation scheduler
- background logging／archive
- PC1 golden regression
- STARTUP／cache／archive regression
- runway registry identity
- consensus runway
- LAND observation-only
- legacy NAV absence
- FDI／ND既存表示・レイアウト
- BANK byte identity

## 未実証事項

この作業環境にはMono/xbuild、KSP 1.12.5／Unity参照DLL、実KSP環境がありません。そのため次は未実施です。

- C#ネイティブコンパイル
- KSP起動
- schema 6→7の実cache移行
- Dull Spot／Goldpool／Glacier Lakeなど実データでのphysical federation
- シーン再生成・KSP再起動間のPhysicalRunwayId不変性
- cache Record／FailureRecord件数不変性
- Approach obstacle snapshotの実生成
- NDへのprocedure描画

Gate 0はソース設計・静的回帰のチェックポイントであり、実機合格ではありません。
