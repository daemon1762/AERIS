# AERIS v0.18.0.0 CP2 KK Runway Axis Registration Hotfix 1 実機試験カード

対象ZIP：

```text
AERISFlightControl-v0.18.0.0_DEV_CP2_KKRunwayAxisRegistrationHotfix1_PreloadFastPath1_CompileHotfix1_Source.zip
```

## 目的

KK／SLE空港について、滑走路線が動かないだけでなく、登録方位そのものが実景滑走路中心線へ一致することを確認する。

## 事前条件

1. 旧DLLを今回ビルドで置換する。
2. 起動後、KK／SLEの再測量が完了するまで待つ。
3. `AERISFlightControl.log`へ`[RUNWAY_AXIS]`が出ることを確認する。
4. 動画、ログ、`AirfieldCertificationCache.cfg`を保存する。

## 試験1：KolaIsland

1. 滑走路を上空から見下ろせる位置へ移動する。
2. NDをNORTH UP、20kmまたは40kmで表示する。
3. RWY 16を選択する。
4. 実景滑走路の両端とND滑走路線を比較する。
5. APP方向をRWY 34へ切り替え、同じ中心線の逆方向になることを確認する。
6. TRACK UPへ切り替え、90度以上旋回する。
7. Rangeを`20 → 40 → 80 → 40 → 20km`と変更する。

合格条件：

- ND線が実景滑走路中心線と同じ角度になる。
- 一端では一致するが反対端で離れる角度ずれがない。
- RWY 16とRWY 34が正確な相互逆方位になる。
- 旋回、Range、Preview／Final置換で相対位置が動かない。
- `[RUNWAY_AXIS] axisRegistrationValid=True`。
- `meshRunwayHeadingDeg`と`registeredHeadingAfterDeg`が1度以内。
- `registeredHeadingBeforeDeg`と実滑走路が異なる場合、`headingCorrectionDeg`が非ゼロになる。
- `[RUNWAY_PLACEMENT] absolutePlacementValid=True`。

## 試験2：複数MOD空港

最低3空港で同じ確認を行う。推奨：

- KolaIsland
- Dundard's Edge
- UberDamまたはKojaveSands

合格条件：

- 各滑走路で実景中心線とND線の角度が一致する。
- 個別空港専用の手書き角度補正を必要としない。
- 滑走路面軸を独立測量できない空港は誤ってCERTされない。

## 試験3：バニラ回帰

KSC Main RunwayとIsland Airfieldを確認する。

合格条件：

- 従来位置・方位から変化しない。
- KK専用Axis Revisionの再測量対象にならない。
- APP方向切替、SELECT、CLEARが正常。

## 異常時に見るログ

```text
[RUNWAY_AXIS]
meshRunwayHeadingDeg=
registeredHeadingBeforeDeg=
registeredHeadingAfterDeg=
headingCorrectionDeg=
runwayDesignatorErrorDeg=
surfaceAspect=
surfacePoints=
axisRegistrationValid=
```

`axisRegistrationValid=False`の場合は安全拒否が成立した状態であり、無理にCERTさせずログを提出する。

## 提出物

- 画面録画
- `AERISFlightControl.log`
- `AirfieldCertificationCache.cfg`
- KolaIslandを上空から見たスクリーンショット
