# AERIS v0.18.0.0 CP3 Gate 5 Candidate 9 実機テストカード

## 対象
Candidate 9 — UI Stability / Telemetry Hotfix 1

## 目的
1. AERIS UIを開くだけでFPSが大幅低下する現象の改善確認。
2. 改行・文字列長・ウィンドウ幅変化でボタン位置/寸法が勝手に変わらないことの確認。
3. 例外は **SYSTEM > AIRFIELDS の空港/滑走路選択行のみ**。この行は空港名等を表示するため大きさ可変を許可する。
4. ND画質・LOD・滑走路位置・操縦系に回帰がないことの確認。

## A. FPS比較
同一シーン・同一カメラ・同一機体で実施する。
- AERIS UI CLOSED: 10秒以上観察
- AERIS UI OPEN（通常SYSTEMページ）: 10秒以上観察
- PRELOAD TERRAINページ OPEN: 10秒以上観察
- UI CLOSEDへ戻す: 10秒以上観察

記録: FPSの概算レンジ、体感スタッター、動画、AERISFlightControl.log、KSP.log。

### 合格
- UI OPENだけを理由とする持続的・大幅なFPS半減級低下が消失している。
- PRELOAD表示数値は最大4 Hz更新でも操作不能・状態誤認を生じない。
- UIを閉じた状態では表示用snapshot処理が走らない。

## B. UI固定幾何テスト
以下を狭幅/標準幅/広幅で操作する。
- MAIN tabs / SYSTEM tabs
- MASTER
- OPTIONS / PERFORMANCE
- PRELOAD mode / speed / storage / body controls
- AIRFIELDS category / RELOAD / WARP / CHECK / MARK / CLEAR
- LAND selector / runway direction / ARM
- NAV / AP / FBW / TAKEOFF controls
- FDI / ND

### 合格
- AIRFIELDSの空港/滑走路選択行以外は、文字列の長さ・改行・状態値・ウィンドウ幅を理由にボタンの幅/高さ/行配置が変化しない。
- ボタン文字が収まらない場合はクリップされ、ボタン自体が勝手に拡張しない。
- AIRFIELDS空港/滑走路選択行のみ可変サイズを許可。
- LAND空港選択は例外ではなく固定。

## C. ND品質回帰
Candidate 8と同条件で地形、海岸線、滑走路、レンジ変更、高速移動を確認する。

### 合格
LOD、地形解像度、GPU rasterizer、滑走路認証/位置、ND表示品質に劣化がない。

## D. 提出物
- AERISFlightControl.log
- KSP.log
- FPSカウンタを含む動画
- 幽霊滑走路の再発有無

CP3はこのruntime受入完了までCLOSEしない。
