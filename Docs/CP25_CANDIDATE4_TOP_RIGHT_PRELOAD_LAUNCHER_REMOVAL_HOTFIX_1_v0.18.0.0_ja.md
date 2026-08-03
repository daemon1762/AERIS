# CP2.5 Candidate 4 Top-Right Preload Launcher Removal Hotfix 1

## 目的

Main Menu、Space Center、VAB、SPHなどで画面右上へ独立表示されていた`AERIS PRELOAD`ランチャーを削除する。
Preload画面は既存のAERIS Toolbarボタンから開き、FULL BOOSTの開始・停止はPreload Mapsページ内のボタンだけで行う。

## 変更

`AERISWindow.DrawPreloadOnly()`は、非FlightでPreloadウィンドウが閉じている場合に何も描画せず終了する。
右上座標へ`GUI.Button`を生成する経路を削除した。

維持する入口：

- Stock Application Launcher／Blizzy Toolbar上のAERISボタン
- ToolbarBridge → `ShowForCurrentScene()`／`HideForCurrentScene()`
- AERIS Preload Terrainウィンドウ内のCloseボタン

維持する操作：

- `START PRELOAD BOOST — FULL`
- `STOP PRELOAD BOOST — FULL`
- BUILD／PAUSE／RESUME／CANCEL／VERIFY／REBUILD／DELETE

## 非変更範囲

Candidate 4の標準Preload、FULL BOOST、PQS供給、CPU圧縮、SSD super-batch、Flight safety、GPU境界、ND／FDI表示ポリシー、AA、AP、PROTECT、LAND、Map DRAM、滑走路データは変更しない。
