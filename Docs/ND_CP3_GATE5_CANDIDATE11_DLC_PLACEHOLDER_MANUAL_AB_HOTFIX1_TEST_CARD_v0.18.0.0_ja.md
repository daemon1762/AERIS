# AERIS v0.18.0.0 CP3 Gate 5 Candidate 11 実機テストカード

## 目的
Making History の Dessert Airfield が `RWY --` の DLC placeholder 状態でも、SYSTEM > AIRFIELDS から手動 A/B 絶対座標を採取できることを確認する。

## 手順
1. Dessert Airfield の滑走路端Aへ機体を停止させる（対地速度 5 m/s 以下）。
2. SYSTEM > AIRFIELDS > PENDING > Dessert Airfield を開く。
3. `MARK A` を押す。
4. 詳細欄の `A/B ABSOLUTE GEO` に A の LAT/LON/ALT が表示され、B は NOT MARKED のままであることを確認する。
5. 反対側の物理滑走路端へ移動し停止する。
6. 同じ Dessert Airfield placeholder を開き `MARK B` を押す。
7. A/B双方の LAT/LON/ALT、物理滑走路長、方位/逆方位が保存されることを確認する。
8. 得られたA/B数値または UserRunwayCalibrations.cfg をAERIS開発へ渡す。

## 期待結果
- DLC runtime geometry が未露出でも MARK A/B が使用可能。
- 座標系は BODY_FIXED_GEODETIC_ABSOLUTE。
- 自動推定座標は生成しない。
- LAND ARM / ND runway presentation はデフォルト統合前には抑止されたまま。
- `CLEAR` で当該DLC空港のユーザー校正だけ消去可能。
