# AERIS v0.18.0.0 DEV CP2 Compile Hotfix 1

## 修正対象

ユーザー環境のxbuildで検出された次のC#コンパイルエラーを修正した。

- `AERISTerrainTileSystem.cs`: `available`が`lock`ブロック内だけで宣言され、ブロック外で参照されていた。
- `AERISNavigationDisplay.cs`: TRAFFIC無効時の短絡評価により`trafficFrame`が未代入となり得た。

## 修正

- `available`を同期ブロック前で0初期化し、同期ブロック内で算出する。
- `trafficFrame`を`null`初期化してから短絡式の`out`取得を行う。
- 上記2件を固定するC# definite-assignment回帰試験をCP2受入へ追加する。

## 変更しない範囲

- Runtime表示とsemantic versionは`v0.18.0.0 DEV CP2`のまま。
- Terrain、ND、AP、BANK、LAND、NAVの機能仕様は変更しない。
- AP／BANK凍結、旧NAV不在、新NAV BLOCKED、LAND無制御権限を維持する。

## 未実施

assistant環境にはKSP／Unity参照DLLとMono/xbuildがないため、ネイティブC#コンパイルは未実施。ユーザー環境での再xbuildが実機ゲートとなる。
