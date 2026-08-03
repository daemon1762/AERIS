# CP2 Manual Runway Designation Grouping Hotfix 1 実機試験カード

## 前提

- 完成済み`UserRunwayCalibrations.cfg`を削除しない。
- Build identity末尾が`MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1`であること。

## 試験

1. KSP起動後、AIRFIELDSを開く。
2. `USER CALIBRATED — MANUAL`カテゴリを開く。
3. 同じ物理滑走路が2行に分かれず、1行で表示されること。
4. 行の形式が概ね次であること。

```text
空港名
RWY NN / RWY NN | 長さ | MANUAL CALIBRATED
```

5. 手動登録前の古いRWY番号ではなく、A/B端点から算出した番号に更新されること。
6. 項目を展開すると、両方向のcourse、Threshold、認証情報がそれぞれ確認できること。
7. MARK A/B/CLEARは物理滑走路につき1組だけ表示されること。
8. NDでは両方向を引き続き個別に選択できること。
9. 再測量後・再起動後も番号とグループ表示が維持されること。
10. 自動/provider滑走路も同一物理滑走路の相反方向が1行にまとまること。

## 期待ログ

```text
[RUNWAY_CALIBRATION] DISPLAY DESIGNATIONS REFRESHED
source=GEOMETRY_HEADING
stableIdsPreserved=True
```

## FAIL条件

- 同じ滑走路が相反方向ごとに2行表示される。
- 古いRWY番号が残る。
- Stable ID変更によりND選択や再起動継承が失われる。
- 一方の方向が一覧またはNDから消える。
