# CP3.75 Candidate6 実機テストカード

## 1. Build identity
起動UIに `DEV CP3.75 — HIGH-DENSITY COASTLINE PRELOAD CANDIDATE 6` が表示されること。

## 2. 既存preload DBの無破壊増築
Candidate5で100%生成済みのDBを残したまま非Flightシーンへ入る。

- 通常terrainの全削除／全REBUILDを要求しないこと。
- `[PRELOAD_COAST_HD] event=QUEUE` が海岸線候補tileに出ること。
- 続いて `event=READY ... resolution=129` が出ること。
- 最終的に各ocean bodyで `event=COMPLETE` へ到達すること。
- Sun/Jool等surface/ocean refinement対象外で無限BUILDにならないこと。

## 3. Flight表示
Kerbin / HHC4等で10・20・160 kmを確認する。

- presentation telemetryで `coast_hd_entries > 0` を確認。
- Candidate5より海岸線の階段感が減ること。
- Candidate3で解消した短冊／可変線幅artifactが再発しないこと。
- coastline lineと33x33 land/water fillのズレが目立たないこと。
- 青一色、blank、例外、無限BUILDを起こさないこと。

## 4. Regression
- RANGEは10/20/40/80/160 kmだけ。
- RG seaは濃い青のまま。
- Terrain QualityはLOW固定、ND updateは10 Hz固定。
- 高速飛行でforced_recoveryが速度比例増殖しないこと。

## 5. Candidate6の既知の限界
Candidate6は33x33でland/water混在を検出できたFAR tileだけを129x129再サンプリングする。粗い33x33が完全に見落とす微小島を新規発見することは本Candidateの要件外。
