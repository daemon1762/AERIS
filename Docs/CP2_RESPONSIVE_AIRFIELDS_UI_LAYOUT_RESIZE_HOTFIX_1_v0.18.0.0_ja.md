# AERIS v0.18.0.0 DEV CP2
## Responsive AIRFIELDS UI Layout / Resize Hotfix 1

## 目的

`AERISFlightControl(21).zip` と `Video_2026-07-26_19-50-07.mkv` で確認された、AIRFIELDS画面の文字重なり、固定高さボタンの文字潰れ、ウィンドウサイズ変更時の急激な跳び、画面外にはみ出す最大高さを修正する。

本Hotfixは表示・レイアウト専用であり、滑走路認証、手動校正、NDデータ、LAND、AP、APP、FlightCtrlStateへ新しい書込みを追加しない。

## 原因

1. 空港行は2行以上の文字列を表示するにもかかわらず、高さを38pxへ固定していた。
2. `CHECK HERE`は狭い幅で3行以上になる可能性があるが、高さを36pxへ固定していた。
3. カテゴリ、SYSTEMタブ、MASTER、Virtual Attitude等も固定22/46pxで、長い文字列を縦へ逃がせなかった。
4. 失敗コードを`ANCHORSURFACEUNRESOLVED`のような連続文字列で表示し、折返し位置を作っていなかった。
5. リサイズは移動中のグリップを基準に`Event.delta`を累積していたため、途中サイズへ合わせにくく、ログ上も不自然に大きいdeltaが記録された。
6. 最大高さ920が実画面高さを上回る環境で、ウィンドウ下端が画面外へ出る可能性があった。

## 修正

- `GUIStyle.CalcHeight`で実文字列・現在幅を測定し、必要な高さへボタンを縦方向に拡張する。
- AIRFIELDS行、カテゴリ、`CHECK HERE`、MASTER、Virtual Attitude、メインタブ、SYSTEMタブを可変高さ化する。
- 狭い幅ではSYSTEMタブを2列へ落とし、AIRFIELDS見出しと`RELOAD / RESCAN`を縦積みにする。
- PascalCaseの失敗コードを空白区切りへ変換する。
- AIRFIELDS内側スクロール領域と外側スクロール領域の予約高さを幅別に調整する。
- リサイズ開始時のマウスポインタをスクリーン座標で固定し、現在ポインタとの差からサイズを計算する。
- 最大幅・最大高さを現在の`Screen.width / Screen.height`以内へ制限する。
- リサイズグリップを42px、フッターを46pxへ拡大する。

## 安全境界

- 滑走路DB、認証状態、校正保存形式、相反滑走路ペアは変更しない。
- ND snapshot publish経路は変更しない。
- 旧NAVを復活させない。
- LANDは引き続き観測専用であり、操縦権限を持たない。
- CP2調査用デバッグ表示を復活させない。
