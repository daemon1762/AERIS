# CP2 Manual Runway Absolute Geodetic Endpoint Authority Hotfix 1

## 目的

手動登録した滑走路端A/Bを、天体固定の絶対緯度・経度・海抜高度として最終権威にする。

## AERISFlightControl(24)で確認した原因

保存ファイルのA/Bは正しかった。

- A: `-4.0581871709924062, -72.08052478927371`
- B: `-4.260410797011267, -72.138091665285287`
- 真のA→B方位: 約`195.848°`
- 距離: 約`2201.38m`

しかし再測量時、A/Bを一度Unity world座標へ変換し、provider local frameのEast/Northへ投影していた。このローカル基底では東西符号が反転し、真の`195.85°`が約`163.67°`へ鏡映された。その結果、保存済みA/Bとは異なる軸がNDへ公開された。

## 修正

1. A/Bは`BODY_FIXED_GEODETIC_ABSOLUTE`として扱う。
2. Unity world座標、floating origin、provider local handednessを経由しない。
3. 天体半径と球面逆解から、参照点に対するEast/Northをdouble精度で生成する。
4. A/B高度は参照海抜との差として保持し、最終地理座標への逆変換で元値へ戻す。
5. 物理mesh・Anchor Scanは幅、表面、整合診断にのみ使用し、手動A/Bの位置・方位を移動しない。
6. 保存CFGへ`coordinateFrame = BODY_FIXED_GEODETIC_ABSOLUTE`を記録する。

## 安全境界

- 自動登録滑走路の既存mesh/Anchor処理は変更しない。
- Kramax Witness処理は変更しない。
- Stable ID、相反2方向、手動カテゴリ分離、RWY番号更新、ND選択系を維持する。
- Kola固有分岐を追加しない。

## 期待ログ

```text
coordinateFrame=BODY_FIXED_GEODETIC_ABSOLUTE
registeredHeadingBeforeDeg=195.85
registeredHeadingAfterDeg=195.85
axisRegistrationDetail=USER BODY-FIXED GEODETIC A/B AXIS — NO MESH/ANCHOR REALIGNMENT
absolutePlacementDetail=BODY-FIXED ABSOLUTE LAT/LON/ALT ENDPOINT AUTHORITY
physicalRunway=RWY 20/02
```
