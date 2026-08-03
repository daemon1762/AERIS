# AERIS v0.18.0.0 CP2 Calibration Round-Trip / Bidirectional Runway Pair Hotfix 1

## 目的

`AERISFlightControl(19).zip`および`Video_2026-07-26_16-45-43.mkv`で確認された、手動滑走路校正の保存失敗を修正する。同時に、手動で記録するThreshold A/Bを別々の滑走路として扱わず、1本の物理滑走路から相反する2方向を必ず生成する。

## 実機で確認された不具合

AIRFIELDS画面でMARK A/Bを実行した際、次のエラーが表示された。

```text
CALIBRATION SAVE FAILED: InvalidDataException — temporary calibration file verification failed
```

KSP/Mono環境では`ConfigNode.Save`後の再読込形状が、名前付きroot、汎用root配下の名前付きchild、汎用rootへの直接値配置のいずれにもなり得る。旧コードは一部の形状だけを正当としたため、正しく保存された一時ファイルを誤って拒否した。

## 修正1：校正保存の完全ラウンドトリップ

- 名前付きroot、名前付きchild、直接値rootの3形状を共通resolverで受理する。
- 一時ファイルを書いた直後に全recordを再読込する。
- 原子move後の正式ファイルも再度読み返す。
- 正式ファイルの読戻しに失敗した場合、直前の`.bak`を復元する。
- record数、body、端点状態、mismatch状態、相反方向情報を検証する。
- 成功ログへ`fullRoundTrip=True`と`committedReadback=True`を出す。

## 修正2：1本の物理滑走路から相反2方向を生成

手動校正の保存単位は単一の物理滑走路である。

```text
Threshold A ============================ Threshold B

Direction A: A -> B
Direction B: B -> A
```

A/Bの2点が揃った時点で、以下を自動生成する。

- A→B方位
- B→A方位（A→B + 180度）
- 双方の滑走路番号
- AをThreshold、BをOpposite Thresholdとする方向
- BをThreshold、AをOpposite Thresholdとする方向
- 双方で別々のStable ID
- 双方のLocalizer中心線および捕捉範囲

両方向は同じ物理滑走路を共有するが、進入地形検証は方向別に独立して実行する。片側に地形障害がある場合、その側だけがFAIL/PENDINGとなり、反対側の安全認証を巻き込まない。

## 保存schema

`UserRunwayCalibrations.cfg`のschemaを3へ更新する。

追加項目：

```text
reciprocalDirectionPair
 directionAHeadingDeg
 directionBHeadingDeg
```

schema 1および2は引き続き読込可能。schema 3の完全な二点校正は、相反方向宣言と両方位が一致しなければfail-closedで無視する。

## 安全境界

- 空港名に依存する分岐は追加しない。
- Kola以外の全手動校正空港へ同じ処理を適用する。
- LANDや操縦権限は追加しない。
- 旧NAVは復活させない。
- 両方向を生成しても、方向別の地形・障害物検証は省略しない。
- ユーザー所有の`UserRunwayCalibrations.cfg`は配布ZIPへ含めない。
- CP2専用デバッグ表示は復活させない。

## 期待ログ

A/B保存成功時：

```text
[RUNWAY_CALIBRATION] save verified; records=...; fullRoundTrip=True; committedReadback=True; reciprocalPairSchema=3.
```

再測量で物理滑走路を方向ペアへ変換した時：

```text
[RUNWAY_CALIBRATION] RECIPROCAL PAIR GENERATED; ...; directionA=RWY xx; directionB=RWY yy; localizerPair=True; approachValidation=INDEPENDENT; ...
```

## CP2状態

このHotfixはCP2最終候補の修正であり、native Mono/xbuildおよびKSP実機試験が完了するまでCP2はOPENのままとする。
