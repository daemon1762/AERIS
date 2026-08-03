# ND CP2 Manual Runway Absolute Geodetic Endpoint Authority Hotfix 1 実機試験カード

## 前提

- 既存の`UserRunwayCalibrations.cfg`を削除しない。
- KolaのA/Bは既存値のまま使用する。
- Build identity末尾が`MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1`であること。

## 試験

1. Kolaへ入り、手動校正を再作成せず再測量する。
2. `USER CALIBRATED — MANUAL`に物理滑走路1項目、相反2方向が表示されること。
3. RWY表示がA/B実方位由来の`RWY 20 / RWY 02`になること。
4. NDの滑走路線が実滑走路中心線と一致すること。
5. A端付近、B端付近、滑走路中央で位置関係を確認すること。
6. 再測量、シーン切替、再起動後も同じ位置を維持すること。
7. ログで次を確認する。

```text
coordinateFrame=BODY_FIXED_GEODETIC_ABSOLUTE
registeredHeadingBeforeDeg≈195.85
registeredHeadingAfterDeg≈195.85
headingCorrectionDeg=0.00
absolutePlacementValid=True
```

## FAIL条件

- `registeredHeadingAfterDeg≈163–164°`へ戻る。
- A/B登録線がmesh軸またはLaunch Anchorへ回転・平行移動する。
- RWY番号が`16/34`へ戻る。
- 手動登録線と実滑走路の中心線が見た目で一致しない。
