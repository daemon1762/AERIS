# 実機テストカード — Top-Right Preload Launcher Removal Hotfix 1

1. KSPを起動してMain Menuへ入る。
2. 画面右上に独立した`AERIS PRELOAD`ボタンが表示されないことを確認する。
3. Stock／Blizzy ToolbarのAERISボタンからPreload画面を開く。
4. Preload Mapsページ内で`START PRELOAD BOOST — FULL`と`STOP PRELOAD BOOST — FULL`を操作する。
5. Closeで閉じた後も画面右上に代替ランチャーが現れないことを確認する。
6. Space Center、VAB、SPHでも同じ動作を確認する。
7. FULL BOOST開始後にFlightへ移り、`FLIGHT_SAFETY`で停止することを確認する。

合格条件：右上独立ボタン0件、Toolbar入口正常、タブ内START／STOP正常、Candidate 4 throughputとFlight safetyに退行なし。
