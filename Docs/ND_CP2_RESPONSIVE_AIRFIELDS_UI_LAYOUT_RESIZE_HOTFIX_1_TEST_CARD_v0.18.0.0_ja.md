# CP2 Responsive AIRFIELDS UI / Resize Hotfix 1 実機試験カード

## 1. Build確認

Build identity末尾が次であること。

`RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1`

## 2. 最小幅試験

1. AIRFIELDS画面を開く。
2. ウィンドウを可能な限り狭くする。
3. SYSTEMタブが2列へ折り返されること。
4. AIRFIELD見出しとRELOADボタンが縦積みになること。
5. CERTIFIED、PROVISIONAL、FAILEDの各カテゴリを開く。
6. 空港名、滑走路名、方位、長さ、状態が隣の行へ重ならないこと。
7. `ANCHOR SURFACE UNRESOLVED`等が単語区切りで表示されること。

## 3. 長文ボタン試験

1. 長い名称の空港・滑走路を開く。
2. 行ボタンが必要なだけ縦へ太くなること。
3. 詳細を開き、`CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY`が欠けずに表示されること。
4. MASTERが長いSTANDBY理由を表示しても文字が潰れないこと。

## 4. リサイズ追従試験

1. ↘グリップをゆっくり右下へ動かす。
2. 途中の任意サイズで止められること。
3. 一度最大化し、次に中間幅へ戻せること。
4. 一度最小化し、次に中間幅へ戻せること。
5. グリップがスクロールバーと重ならないこと。
6. ウィンドウ下端が画面外へ出ないこと。
7. KSP再起動後も最後に保存したサイズを復元すること。

## 5. 回帰

- NDに空港・滑走路が表示されること。
- Kola手動校正の相反2方向が維持されること。
- `ERROR`、例外、GUILayout mismatchがないこと。
- CP2デバッグ候補表示が復活していないこと。
