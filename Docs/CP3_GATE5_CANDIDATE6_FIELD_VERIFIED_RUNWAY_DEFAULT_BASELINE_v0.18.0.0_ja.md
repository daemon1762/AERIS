# CP3 Gate 5 Candidate 6 — Field-Verified Runway Default Baseline

2026-08-02に実機上でA/B両端を確認して登録した非Stock滑走路40本を、AERIS同梱のデフォルト手動authorityとして採用する。

- 配布原本: `GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg`
- 新規環境で `PluginData/UserRunwayCalibrations.cfg` が存在しない場合だけ初期seedする。
- 既存のユーザー校正ファイルは上書きしない。
- データの意味は `USER CALIBRATED` のままであり、自動認証へ格上げしない。
- Base-game Stockのみ自動authorityを許可するCandidate 5方針は維持する。

収録データは40 physical runway / 80 reciprocal direction。全レコードがBODY_FIXED_GEODETIC_ABSOLUTE A/B、reciprocal pair、有限座標を持つ。
