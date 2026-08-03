# v0.17.0.2 実KSP・最終証拠取得カード

目的: IDホットフィックスが実環境でレジストリ全体をcommitできることを証明する。  
このカードでは自動着陸の安全性を試験しない。

## 1. 事前条件

- v0.17.0.2をKSP参照DLLでビルド済み
- KSPを完全終了してからDLLを交換
- AERIS DLLが一つだけ存在
- `GameData/AERISFlightControl/Airfields`、設定、FlightPlans、FlightData、Logsを削除しない
- AERISログ、CVR、KSP.logの保存先に十分な空き容量がある
- LANDは観測専用のまま。飛行制御を追加しない

## 2. 起動走査

1. KSPを起動する。
2. 飛行シーンへ入る。
3. `SYSTEM > AIRFIELDS`を開く。
4. `DISCOVERING / SURVEYING / VALIDATING`の終了を待つ。
5. 画面を保存する。

合格条件:

- `STATE COMPLETE`
- `DATABASE REV >= 1`
- `LAST`が`NEVER`以外
- `AIRFIELDS`、`RUNWAYS`が0ではない
- KSC Main RunwayとIsland Airfieldが別施設として存在
- KSC 09/27、Island 09/27が表示
- ログに`atomic commit cause=STARTUP`
- `DISC_STOCK_KSP`がない

ここで表示される`CERTIFIED`は現行の幾何/粗地形判定であり、精密着陸安全認証とは扱わない。

## 3. 手動走査2回

各回の前後で次を記録する。

```text
generation
database revision
airfield count
physical runway count
approach direction count
state/result
```

`RELOAD / RESCAN`を1回押し、COMPLETEまで待つ。もう一度同じ操作を行う。

合格条件:

- 2回とも`atomic commit cause=MANUAL`
- revisionが毎回増える
- 2回の最終件数が一致
- Stock/DLCの独立施設が混ざらない
- KK/SLEの同一空港内滑走路だけが同一airfieldへまとまる
- duplicate、例外、全件0がない

## 4. 施設別証拠

`02_RUNWAY_BY_RUNWAY_RESPONSE_ja.md`の全行について、少なくとも次を保存する。

- 検出/未検出
- Provider site/group/model
- 選ばれたsurvey method
- 物理滑走路数
- 各進入方向
- state/failure code/detail
- snapshot最大slice

現行コードではsurvey method強制が不十分なので、`ManualRequired`または`PairedThresholds`が自動認証された場合は合格にせず、「方式強制不良」と記録する。

## 5. 禁止事項

- 旧`DISC_STOCK_KSP`衝突を避けるために重複検査を無効化しない
- 認証件数を増やすために閾値、幅、証拠数、地形余裕を緩めない
- X字滑走路の軸を自動で一つ選ばない
- 片方向不合格を反対方向へ無条件伝播しない
- 3.0°で不合格だからという理由だけで3.0°未満へ下げない
- この試験中にAP/BANKを変更しない

## 6. 取得物

- 起動時、手動1回目、手動2回目のAIRFIELDS画面
- 完全な`KSP.log`
- 完全なAERISログ
- 完全な`cvr_events.csv`
- KSP/DLC/KK/SLE/AERIS版一覧
- 全施設の結果表
- ZIPとSHA-256

KSP終了後に原本を保持したままZIP化し、展開検査とバイト照合に合格してから引き渡す。

## 7. Ubuntuビルド経路

デスクトップ:

```bash
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

ノートPC:

```bash
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

