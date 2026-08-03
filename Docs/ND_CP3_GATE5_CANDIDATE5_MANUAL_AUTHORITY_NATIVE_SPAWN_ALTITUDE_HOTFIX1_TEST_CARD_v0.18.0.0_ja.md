# CP3 Gate 5 Candidate 5 実機テストカード

## 1. 認証authority
- KSC Main / Island Airfield（base-game Stock）が従来どおり利用可能か確認。
- DLC / KK / SLE / UserCfgの未手動登録滑走路が自動Certifiedとして選択可能にならないこと。
- 詳細欄に `MANUAL A/B ... REQUIRED` が表示されること。
- 非StockではCHECK HEREによる自動geometry検証を使わず、MARK A / MARK Bのみで登録する。
- A/B完了→RESCAN後、`USER CALIBRATED — MANUAL`へ入り双方向RWYが生成されること。
- 再起動しても古い自動認証cacheが復活しないこと。

## 2. Native Spawn Warp
- SandboxでKK/SLE滑走路を開き `WARP TO MOD NATIVE SPAWN` を1回押す。
- 数千m上空へ出現せず、native spawn付近の低い高度からPhysics Easingされること。
- ログの `[AIRFIELDS/NATIVE_SPAWN_WARP]` で `native_alt_asl`, `terrain_alt_asl`, `target_agl` を確認。
- target_aglが有限かつ通常は数m～数十m程度であること。
- 12秒以内の連打が拒否されること。
- Career / Scienceではワープボタンが表示されないこと。

## 3. Gate 5継続
- ND/Terrainに新規回帰がないこと。
- `cpu_terrain_draw=0`。
- 黒欠け、クソコラ化、滑走路浮遊追従を再確認。
