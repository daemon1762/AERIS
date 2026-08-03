# ND CP2 Manual Calibration Reflection Hotfix 1 実機試験カード

## 前提

- 本Hotfixをnative Mono/xbuildでビルドする。
- `UserRunwayCalibrations.cfg`は削除しない。
- 既存の完成済みA/B校正がある場合はそのまま起動する。

## 1. Build identity

ログ末尾に次を確認する。

`MANUAL CALIBRATION REFLECTION HOTFIX 1`

## 2. 保存済み校正の読込

ログで以下を確認する。

- `[RUNWAY_WITNESS] ... USER 1`以上
- `basis=UserCalibrated`
- `RECIPROCAL PAIR GENERATED`
- `absolutePlacementValid=True`

AIRFIELDS上部のCALIBRATION表示が過去の`PLACEMENT MISMATCH QUARANTINED`ではなく、`COMMITTED USER RUNWAY(S) ... RECIPROCAL PAIRS READY`になること。

## 3. AIRFIELDS反映

- `USER CALIBRATED — MANUAL`が`CERTIFIED — AUTOMATIC / PROVIDER`より上にある。
- 完成済み手動ペアの2方向が手動カテゴリに表示される。
- 同じ物理滑走路の旧自動FAILED／PROVISIONAL方向が通常カテゴリへ重複表示されない。
- 通常起動時は各カテゴリが閉じている。
- MARK A/Bを新規完了した直後だけ、手動カテゴリが自動で開き、該当ペアが確認できる。

## 4. ND反映

- AIRFIELD DBのCertified runway数とND右上のRWY数が一致する。
- 手動A/B端点から生成した滑走路線が実滑走路へ一致する。
- 双方の方向が選択可能で、方向切替時にThresholdが反転する。
- 旧自動geometryの位置へ線が戻らない。

## 5. 再測量・再起動

- RELOAD / RESCAN後も手動ペアが維持される。
- KSP再起動後も同じ位置・方位・2方向を維持する。
- 自動provider方向が再びFAILED枠へ現れない。

## 合格条件

上記すべて成立し、例外、ERROR、ND永久`NAV DATA`、手動ペア消失がないこと。
