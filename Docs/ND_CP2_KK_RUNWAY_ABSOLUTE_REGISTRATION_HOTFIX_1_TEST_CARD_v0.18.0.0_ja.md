# AERIS v0.18.0.0 CP2 KK Runway Absolute Registration Hotfix 1 実機試験カード

対象ZIP：

```text
AERISFlightControl-v0.18.0.0_DEV_CP2_KKRunwayAbsoluteRegistrationHotfix1_PreloadFastPath1_Source.zip
```

## 目的

Kerbal Konstructs／Stock Launchsites Expansionの滑走路について、ND投影が動かないだけでなく、登録された滑走路中心線そのものが実景の舗装滑走路へ一致することを確認する。

## 事前条件

- 旧DLLを今回ビルドで置換する。
- 起動後、AIRFIELDS再測量が完了するまで待つ。
- ログに`[RUNWAY_PLACEMENT]`が出ることを確認する。
- AERISログと画面録画を必ず保存する。

## 試験1：KolaIsland RWY 34

1. KolaIsland近傍へ移動する。
2. NDを20kmまたは40km、NORTH UP／TRACK UPの双方で表示する。
3. RWY 34を選択する。
4. 実景滑走路の中心線とND滑走路線の横位置を比較する。
5. 90度以上旋回し、レンジを`20 → 40 → 80 → 40 → 20km`と変更する。

合格条件：

- 滑走路線が実景の舗装滑走路中心へ固定される。
- 旋回・レンジ・Preview／Final置換で動かない。
- `[RUNWAY_PLACEMENT] absolutePlacementValid=True`。
- `launchCrossAfterM`が0m近傍。
- LOC／GSが正しい側だけに表示される。

## 試験2：複数MOD空港

最低3空港で同じ確認を行う。推奨：KolaIsland、Dundard's Edge、UberDamまたはKojaveSands。

合格条件：

- 各空港のND滑走路が実景滑走路中心へ一致する。
- 異常なLaunch TransformではCERTされず、LAND ARMが拒否される。
- 個別空港専用の手書き座標補正を必要としない。

## 試験3：バニラ回帰

KSC Main RunwayとIsland Airfieldを確認する。

合格条件：

- 従来位置から移動しない。
- KK専用補正がバニラ滑走路へ適用されない。
- LAND表示、APP方向切替、SELECT、CLEARが正常。

## 提出物

- 画面録画
- `AERISFlightControl.log`
- `AirfieldCertificationCache.cfg`
- 可能ならKolaIslandを滑走路中心線上から見たスクリーンショット
