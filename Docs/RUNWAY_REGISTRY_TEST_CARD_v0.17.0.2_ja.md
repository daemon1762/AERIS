# v0.17.0.2 滑走路レジストリ実KSP試験カード

対象: v0.17.0.2 Runway Registry Identity Hotfix Source RC  
目的: バニラを含む滑走路データベースの起動時・手動再読込コミットを実KSPで確認する

## 事前条件

- v0.17.0.2をKSP参照DLLでビルド済み
- KSPを完全終了してから新DLLを導入
- ユーザーの`GameData/AERISFlightControl/Airfields`、設定、FlightPlans、FlightData、Logsは削除しない
- 旧DLLが重複していない
- 試験中はAERISログとKSP.logを保存する

## 1. 起動時再読込

1. KSPを起動し、飛行シーンへ入る。
2. `SYSTEM > AIRFIELDS`を開く。
3. `DISCOVERING`／`SURVEYING`／`VALIDATING`が終わるまで待つ。

合格:

- `STATE COMPLETE`
- `DATABASE REV`が1以上
- `LAST`が`NEVER`以外
- `RESULT COMPLETE — REV ...`
- `AIRFIELDS`、`RUNWAYS`、`CERTIFIED APPROACHES`が0ではない
- 少なくともKSC Main RunwayとIsland Airfieldが存在
- KSC RWY 09／27、Island RWY 09／27が表示
- ログに`[AIRFIELD_RELOAD] atomic commit cause=STARTUP`がある
- ログに`DUPLICATE AIRFIELD ... DISC_STOCK_KSP`がない

不合格:

- `STATE FAILED`
- `DATABASE REV 0`
- `LAST NEVER`
- 全件0
- `RESULT`に重複、無効geometry、provider例外が出る

## 2. 手動再読込の反復

1. 現在の件数と選択中airfield/directionを記録する。
2. `RELOAD / RESCAN`を1回押す。
3. COMPLETEまで待つ。
4. 同じ操作をもう1回行う。

合格:

- 各回でgenerationが増加
- 各回でdatabase revisionが増加
- 各回で`atomic commit cause=MANUAL`
- 2回の最終件数が同じ
- 選択可能な認証済み進入方向が消えない
- `DISC_STOCK_KSP`重複がない

## 3. Provider別確認

環境に導入されているProviderだけを判定する。

- Stock: KSC Main Runway、Island Airfield
- DLC: Dessert Airfield、Woomerang（DLC導入時）
- Kerbal Konstructs: AIRFIELDS一覧とログで検出状態を確認
- Stock Launchsites Expansion: AIRFIELDS一覧とログで検出状態を確認
- User CFG: ユーザー定義が保持され、上書き・削除されていないこと

KK/SLEは、同じ飛行場の複数滑走路が一つのairfield配下へまとまること。Stock/DLCの独立施設は互いに別airfieldであること。

## 4. LAND安全境界

1. 認証済み方向を選択してLANDをARMする。
2. ARM中に手動再読込する。
3. 再読込完了後、ARMを解除する。

合格:

- ARM中の滑走路、方向、幾何、database generationが変化しない
- 再読込結果は待機し、ARMを勝手に解除しない
- DISARM後に新revisionが一括反映される
- LANDがFlightCtrlState/AP出力を取得しない

## 5. AP/BANK回帰

この修正はAP/BANKを変更していない。通常飛行で既存のBANK受入を短く確認する。

- 20°右BANKへ滑らかにロールイン
- 目標前に入力を緩める
- オーバーシュートせず20°を捕捉
- 捕捉時ロールレートがほぼ0
- 保持中にマイクロウォブルがない

## 記録するもの

- AIRFIELDS画面の起動時、手動1回目、手動2回目のスクリーンショット
- AERISログ
- KSP.log
- KSP版、導入DLC、KK/SLE版
- 起動時／手動各回のairfield、runway、certified approach件数
- 合否と、失敗時の`RESULT`全文
