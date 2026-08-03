# CP2.5最終クローズ 省再起動テストカード

KSP起動は1回だけ。

## 1. 非Flight UI

Main MenuまたはSpace CenterでAERIS Preloadを開く。

- `PRELOAD STANDARD — CP2.5 FINAL`が表示される。
- FULL BOOSTのSTART／STOPボタンが存在しない。
- 右上独立Preloadランチャーが存在しない。
- mode、speed、storage、天体別BUILD／PAUSE／RESUME／CANCEL／VERIFY／REBUILD／DELETEを操作できる。

## 2. STANDARD進捗

未完成tileがある天体で60秒観察する。

- tiles completeまたはMap DRAM revisionが増加する。
- `required-drop=0`。
- Terrain Blockは`outstanding <= 96`。
- Encode capは32以下。
- SSD job capは1。
- `PRELOAD_RECOVERY`が連続発生しない。

## 3. Flight切替

Flightへ入り、10秒後にSpace Centerへ戻る。

- Flight中は`PRELOAD SUSPENDED / FLIGHT READ PRIORITY`。
- 非Flightへ戻ると再起動なしでSTANDARD進捗が再開する。

## 4. AIRFIELDS

SYSTEM → AIRFIELDSを開く。

- 件数0の全カテゴリがグレーアウトされる。
- 0件カテゴリは展開できない。
- 直下に`None.`や巨大な空白が出ない。

## 5. 終了ログ

- AERIS例外なし。
- `requiredDropped=0`。
- `[CP2.5/MAP_DRAM_SUMMARY] ... synchronousSSD=0 ... result=PASS`。
- `callback owned by a destroyed AERISFlightControl:SyncModuleControlSurface instance`が0件。
