# CP3.75 Candidate8 実機テストカード

## 目的
Candidate7の129×129全面fillで発生した2 FPS級の負荷を除去しつつ、高密度海岸線の品質を残す。

## 最優先試験
1. KSC地上、RANGE 20 km、Candidate7と同じ構図。
2. FPSを確認。2 FPS級なら即FAIL。
3. 海岸線と赤/青fill境界を見る。巨大な33×33階段が減り、Candidate7の長方形HDパッチが出ないこと。
4. 160 kmでも確認。

## ログ確認
`[AERIS][TERRAIN_PRESENT]` で以下を見る。
- `coast_hd_entries > 0`
- `coast_sparse_entries > 0`（該当海岸tileが可視なら）
- `coast_sparse_parents` が有限で増殖し続けない
- `forced_recovery` が定常飛行中に暴走しない

## PRELOAD
Candidate7の仕様を維持。通常terrainだけ終わっても100%にしない。coastal phase完了後に初めて100%。
