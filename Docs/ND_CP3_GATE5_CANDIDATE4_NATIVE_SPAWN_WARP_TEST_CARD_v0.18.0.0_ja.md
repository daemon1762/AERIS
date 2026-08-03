# CP3 Gate 5 Candidate 4 — Native Spawn Warp Utility 実機テストカード

## 目的
MOD滑走路登録巡回のため、AERIS独自の滑走路端スポーンを作らず、MODが元々持つ機体スポーン地点へ直接移動できることを確認する。

## Sandbox
1. SandboxでFlightへ入る。
2. SYSTEM → AIRFIELDSを開く。
3. KK/SLEの登録済み物理滑走路を1件展開する。
4. `WARP TO MOD NATIVE SPAWN` が**1個だけ**表示されることを確認する。09/27等の方向別ボタンはFAIL。
5. ボタンを押す。
6. MODから通常スポーンした時の地点・向きと一致することを確認する。
7. Airport/RWYの選択、認証状態、A/B補正状態がワープだけでは変化しないことを確認する。
8. 複数のMOD滑走路で繰り返す。

## Career / Science
- 同じAIRFIELDS詳細を開いてもワープボタンが表示されないこと。表示された時点でFAIL。

## Terrain回帰
- Candidate 3 Generation Bridgeを同時確認する。
- CPU terrain draw = 0。
- 黒抜け、クソコラTemporal warp、滑走路浮遊追従の再発がないこと。

## 合格条件
- 物理滑走路ごとにボタン1個。
- MOD純正spawn transformへのワープ。
- 侵入方向依存なし。
- Career/Science非表示。
- 既存登録/ND/Terrain挙動に副作用なし。
