# AERIS v0.18.0.0 CP2 — KK Runway Axis Registration Hotfix 1

## 目的

Kerbal Konstructs（KK）およびStock Launchsites Expansion（SLE）の一部空港で、滑走路表示が地図上を移動しなくなった後も、登録された滑走路方位が実景滑走路面に対して一定角度ずれていた不具合を修正する。

本Hotfixは画面上の線を個別補正するものではない。認証前の滑走路測量で、実配置済み滑走路床面から独立した物理軸を再抽出し、その軸だけを正式登録へ使用する。

## 旧不具合

旧処理はKKモデル由来のProvider heading、Launch Transform forward、Mesh PCA軸が同じTransform階層の影響を受ける場合、同じ誤方位同士を比較して`headingErrorDeg=0.00`と判定できた。

前HotfixのLaunch Transform拘束は中心線の横位置だけを補正し、滑走路軸を回転させなかった。そのため角度ずれには効果がなかった。

## 新しい方位測量

KK／SLEでは次の役割を分離する。

```text
実配置済み滑走路床面Mesh／Collider
→ 方位、長さ、幅、物理端点

Launch Transform位置
→ 絶対中心線アンカー

Provider heading／RWY番号
→ 広い妥当性確認と診断値
```

### 物理滑走路面の抽出

軸推定候補から以下を除外する。

- Taxiway
- Apron／Ramp
- Platform／Foundation
- Obstacle／Building
- Natural surface
- Approach light

Runway、Centerline、Pavementを優先する。KK側の命名が不十分でFacility全体が一つのMeshの場合に備え、Providerが明示的に固定翼滑走路と判定したStaticでは未分類点も限定的に候補へ残す。

### ロバスト軸探索

1. Provider/RWY方位の前後±20度を0.5度刻みで探索候補にする。
2. 信頼できる細長い物理Primitiveと初期PCA軸も候補へ加える。
3. 各候補方位で、滑走路幅相当の最密帯を抽出する。
4. 長手方向を16～48区間へ分割し、被覆率と支持点密度の均一性を評価する。
5. 一端だけに密集するエプロンや斜めに横切る帯を、密度変動係数で減点する。
6. 最良帯の点群だけでPCAを再実行し、最終物理軸を決定する。

これにより、同じMeshへ付属するエプロンが全体PCAを引っ張る場合でも、長く連続した滑走路床面を優先する。

## 認証ゲート

KK／SLEの認証には以下を必須とする。

```text
独立滑走路面軸：存在
支持点数：16点以上
幾何Aspect：4.0以上
候補軸と物理軸：1.0度以内
物理軸とRWY/Provider方位：15度以内
Launch Transform位置：物理滑走路長手範囲内
Launch Transform横補正：上限内
```

独立した滑走路面軸が得られない場合、Provider headingやLaunch headingだけでCERTしてはならない。

失敗時は安全側で以下を拒否する。

- CERT
- LAND ARM
- LOC／GS表示
- Runway Track Token
- 将来の自動着陸入力

## 登録値と診断

`[RUNWAY_AXIS]`へ以下を出力する。

```text
meshRunwayHeadingDeg
launchTransformHeadingDeg
registeredHeadingBeforeDeg
registeredHeadingAfterDeg
headingCorrectionDeg
runwayDesignatorErrorDeg
surfaceAspect
surfacePoints
axisRegistrationValid
```

`registeredHeadingBeforeDeg`は従来登録に相当するProvider/RWY方位、`registeredHeadingAfterDeg`は実滑走路面から得た新登録方位である。

Launch Transformの方位は診断テレメトリのみで、物理軸の正解として認証へ使用しない。

## キャッシュ

- 全体認証Algorithm：1680を維持
- KK Absolute Placement Revision：2
- KK Axis Registration Revision：1

Source Fingerprintへ`KK_RUNWAY_AXIS_REGISTRATION`を追加し、KK／SLE対象だけを再測量する。正常なバニラ滑走路キャッシュを無関係に失効させない。

## 非変更範囲

- BANK
- HDG
- PITCH
- V/S
- ALT
- ACC
- VEL
- Ground Stability
- Preload Fast Path 1
- Runway Map Lock投影
- LAND操縦権限

## 完成条件

KolaIslandを含む複数MOD空港で、ND滑走路線が実景滑走路の両端・中心線へ一致し、旋回・Range変更・LOD置換でも動かないこと。バニラKSC／Island Airfieldに回帰がないこと。
