# AERIS v0.18.0.0 CP3.5 Gate 1 Candidate 1 実機試験カード

## 1. ビルド確認
ゲーム内AERISタブ上部のcheckpointが次であること。

`DEV CP3.5 GATE 1 — PRESENTATION CADENCE / RESPONSIVE UI CANDIDATE 1`

## 2. ND 160 km 性能試験（最重要）
1. Candidate 14最終試験と同等の機体・描画設定を使用する。
2. NDを160 kmへ設定する。
3. 約2100 m/sまで加速し、十分な時間連続飛行する。
4. ND ON区間とND OFF区間のFPS/frame timeを動画または同条件ログで比較する。
5. AERISログの`[CP3_GATE4C_VIRTUAL_DETAIL]`を確認する。

必須:
- `forced_recovery=0`
- `forced_recovery_suppressed`は高速移動中に増加してよい（むしろGate 1抑止が動いている証拠）。
- `back_cadence_s=0.50`が160 kmで確認できる。
- steady-stateの`back_render`増分が2回/秒を超えないこと（初期FRONT生成/ユーザー操作直後は区間から除外）。
- `cpu_terrain_draw=0`
- Terrain coverage 1.0を維持できること。
- 黒抜け、phantom runway、滑走路と地形の相対ズレがないこと。

## 3. 全range回帰
5 / 10 / 20 / 40 / 80 / 160 kmを順番に切替える。

期待cadence:
- 5/10/20 km: 0.20 s
- 40 km: 0.25 s
- 80 km: 0.33 s
- 160 km: 0.50 s

各rangeで:
- TRACK UP / NORTH UP
- PLAN移動 / RECENTER
- Terrain AUTO / TOPO / REL / OFF
を確認する。

## 4. Presentation latch整合性
高速移動でFRONTがLATCHEDになる区間を観察する。

必須:
- 地形だけが画面内でwarpしない。
- runway/traffic/ownship等world-fixed symbolが同じlatched projection authorityへ従う。
- runway threshold markerが浮遊しない。
- 新BACK完成後は自然にDIRECTへ戻る。

## 5. AERISメインウインドウ・リサイズ回帰
1. AERISメインウインドウを最小幅/高さ付近へ縮小する。
2. 任意の中間サイズへ拡大する。
3. 最大付近まで拡大する。
4. 再び中間サイズへ戻す。
5. FLIGHT CONTROL / PROTECT / AUTOPILOT / SYSTEM / EXTEND ADDONSを巡回する。

必須:
- ボタン/SelectionGrid等の寸法がウインドウサイズに応じて滑らかに変わる。
- タブは3列のrow topologyを維持し、幅thresholdを跨いだだけで突然別座標へ飛ばない。
- スクロール領域やAIRFIELDS表示領域も特定幅を跨いだ瞬間に段差変化せず、連続的に追従する。
- 状態文字列、STANDBY理由、空港名等が変化してもボタンの高さ/座標が勝手に変わらない。
- 自動折返しをしない。長文はclipされてよい。空港行の明示的な2段表示（コードで指定した改行）は対象外で、文字量による自動折返しだけを禁止する。
- AIRFIELDS行も内容によるCalcHeightを行わない。
- ↘リサイズグリップがスクロールバーと重ならず、途中サイズで正確に止められる。
- 再起動後に保存サイズが復元される。

## 6. 回帰境界
- 41 physical runways / 82 directions維持。
- non-stock AUTO CERTIFIEDなし。
- Making History無しならDessert非表示。
- PreloadはON/OFF＋BUILD/PAUSE-RESUME/DELETE/REBUILDのみ。
- Laythe等body transition後にNDが再構築される。
- AP/FBW/PROTECTの操縦挙動に変更がない。

## 提出推奨
- `AERISFlightControl.log`を含むログZIP
- `KSP.log`
- 160 km高速試験の動画/FPS証拠
- リサイズ試験の短い動画またはスクリーンショット
