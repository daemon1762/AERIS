# CP2 Manual Runway Designation Grouping Hotfix 1

## 目的

手動二点校正後の滑走路表示を、記録前のprovider名や古いRWY番号ではなく、A端/B端から確定した実中心線方位に基づく番号へ更新する。

同じ物理滑走路の相反2方向をAIRFIELDS一覧で別項目にせず、1つの物理滑走路項目へまとめる。

表示例：

```text
KSC
RWY 09 / RWY 27
```

## 実装

- staged database検証前にUserCalibrated方向の表示名を再正規化する。
- 各方向は`RWY NN`、物理滑走路は`RWY NN/NN`へ更新する。
- RWY番号は保存済み文字列ではなく、各方向の`HeadingDeg`から毎回算出する。
- Stable ID、選択ID、cache identityは変更しない。
- AIRFIELDSカテゴリの件数と行は方向数ではなく物理滑走路数を基準とする。
- 行の1段目に空港名、2段目に相反方向をまとめて表示する。
- 展開時は両方向の進入情報を独立表示し、校正操作は物理滑走路につき1組だけ表示する。
- 手動校正カテゴリと自動/providerカテゴリの分離は維持する。

## 安全境界

- 滑走路geometry、Threshold、Stable ID、認証状態、ND選択可能性は変更しない。
- LAND/AP/APP/操縦系への書込みは追加しない。
- Kola固有分岐は追加しない。
- 旧NAVおよびCP2調査用デバッグ表示を復活させない。

## CP2状態

本Hotfixはnative Mono/xbuildおよびKSP実機確認前のため、CP2はOPENを維持する。
