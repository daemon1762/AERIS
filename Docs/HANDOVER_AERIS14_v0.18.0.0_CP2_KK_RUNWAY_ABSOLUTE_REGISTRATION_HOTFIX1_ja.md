# AERIS14 引き継ぎ — CP2 KK Runway Absolute Registration Hotfix 1

## 基準原本

`AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayPresentationUIHotfix1_PreloadFastPath1_Source`

## 不具合

GPU地形と滑走路の相対移動は解消したが、Kerbal Konstructs系の一部空港では、認証済み滑走路座標が実景滑走路から一定量ずれたまま固定された。バニラ滑走路は正常だった。

## 根本原因

旧測量はLaunch Transformを測量座標原点と運用証拠の両方に使用していた。そのため、実配置Static／Mesh／Colliderの中心線とLaunch Transformの横位置関係を独立検証できず、誤った絶対位置でもCERT可能だった。

## 実装

- KK／SLEでは実配置Static instance originを測量基準にする。
- Launch Transformを独立した絶対中心線拘束として保持する。
- 測定中心線の横誤差だけをLaunch Transformへ補正する。
- Launch Transformの方位差、滑走路長手範囲、最大補正量を検証する。
- 不整合時は`AbsolutePlacementInvalid`で認証拒否する。
- `[RUNWAY_PLACEMENT]`へ補正前後の横誤差、長手位置、方位差、補正量を記録する。
- `ABSOLUTE_PLACEMENT`と`LAUNCH_CROSS_TRACK`を認証パラメータへ追加する。

## キャッシュ

全体の認証アルゴリズムは1680を維持する。KK／SLEだけSource Fingerprintへ`KK_ABSOLUTE_PLACEMENT`とAbsolute Placement Revision 1を加えるため、対象キャッシュのみ再測量される。バニラキャッシュを無関係に失効させない。

## 安全境界

- AP、BANK、HDG、PITCH、V/S、ALT、ACC、VEL、Ground Stabilityは変更しない。
- LANDへ操縦権限を追加しない。
- 絶対配置が検証できないMOD滑走路はCERT、LAND ARM、LOC／GS、Track Tokenへ渡さない。
- 空港別の手書きオフセット表は導入しない。

## 未実施

作成環境にはMono／KSP参照DLL／Unity実行環境がない。native C#ビルドと実機KK空港測量はユーザー環境で行う。

## 再開位置

実機試験カードに従いKolaIslandと複数MOD空港を検証する。合格までCP2はOPEN、CP3はBLOCKED。
