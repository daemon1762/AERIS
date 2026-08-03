# AERIS14 引き継ぎ — CP2 KK Runway Axis Registration Hotfix 1

## 基準原本

`AERISFlightControl-v0.18.0.0_DEV_CP2_KKRunwayAbsoluteRegistrationHotfix1_PreloadFastPath1_Source`

## ユーザー実機確認

- 滑走路が地図上を動く問題：解消済み
- バニラ滑走路：正常
- 一部KK空港：登録滑走路線の方位角が実景滑走路面と不一致
- KolaIslandでは一端付近から長手方向へ進むほどND線が実滑走路から離れた

## 前Hotfixが効かなかった理由

Launch Transform拘束は滑走路中心線を横方向へ平行移動するだけで、`AxisEast / AxisNorth`とHeadingを回転させなかった。

また、Provider headingとLaunch Transform forwardが同じKK Transform階層由来の場合、同じ誤方位同士を比較して`headingErrorDeg=0.00`になり、独立検証にならなかった。

## 今回の実装

- 実配置済み滑走路面点群から独立物理軸を抽出する。
- Taxiway、Apron、Platform、Obstacle、Natural Surface、Approach Lightを除外する。
- Provider方位±20度、物理Primitive、初期PCAを探索候補にする。
- 候補ごとに滑走路幅相当の最密帯を抽出する。
- 長手区間の被覆率と支持密度の均一性を評価し、エプロン斜め帯を減点する。
- 最良帯だけでPCAを再実行する。
- 実滑走路面軸に一致しない候補は認証しない。
- Provider/RWY方位は15度の広い妥当性ゲートと診断値に限定する。
- Launch headingはテレメトリのみとし、物理軸の正解には使わない。
- Launch Transform位置による絶対中心線拘束は、物理軸決定後に適用する。

## キャッシュ

- `CurrentAbsolutePlacementRevision = 2`
- `CurrentAxisRegistrationRevision = 1`
- Source Fingerprintへ`KK_RUNWAY_AXIS_REGISTRATION`を追加

KK／SLEだけ再測量し、バニラ滑走路を無関係に再認証しない。

## 安全境界

独立滑走路面軸が得られない、支持不足、Aspect不足、設計方位と15度超不一致、Launch位置拘束失敗の場合は`AbsolutePlacementInvalid`として安全拒否する。

拒否対象：CERT、LAND ARM、LOC／GS、Runway Track Token、将来自動着陸入力。

LANDへ操縦権限は追加していない。

## 凍結範囲

BANK、HDG、PITCH、V/S、ALT、ACC、VEL、Ground Stability、Preload Fast Path 1、Map Lock投影を変更しない。

## 未実施

作成環境にはMono／KSP／Unity参照DLLがなく、native C#ビルドと実機KK Mesh測量は未実施。静的・数値試験は実施するが、KolaIslandでの実方位一致はユーザー環境で証明する。

## 再開位置

実機試験カードに従いKolaIslandを最優先で確認する。合格までCP2はOPEN、CP3はBLOCKED。
