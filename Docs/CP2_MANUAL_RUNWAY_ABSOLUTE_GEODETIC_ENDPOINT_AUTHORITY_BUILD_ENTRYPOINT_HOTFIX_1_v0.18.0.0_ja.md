# CP2 Manual Runway Absolute Geodetic Endpoint Authority — Build Entrypoint Hotfix 1

## 症状

ユーザー環境で、配布ZIPの手動実行によるCP2受入は`55/55 scripts PASS`した。しかし、その直後に`build_ubuntu.sh`が生成Assembly表示文字列を一世代前の`MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1`で上書きした。

そのため、ビルドスクリプト内で再実行された静的受入は次で停止した。

```text
FAIL: generated display identifies CP2 manual runway designation grouping hotfix 1
[v0.18.0.0 CP2 static package verification] 167/168 PASS
```

Mono/xbuildの`CoreCompile`には到達していない。

## 根本原因

`Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs`はAbsolute Geodetic Endpoint Authority Hotfix 1を含んでいたが、`build_ubuntu.sh`の`DISPLAY=`がManual Runway Designation Grouping Hotfix 1で止まっていた。

ビルド開始時に`build_ubuntu.sh`が生成ファイルを書き直すため、配布時には正しかったidentityがビルド直前に退行した。

## 修正

- `build_ubuntu.sh`の`DISPLAY`を現行identityへ同期。
- 生成C# identity、AVC metadata、READMEを現行checkpointへ同期。
- build scriptと生成C# identityを直接比較する専用退行試験を追加。
- identity生成がacceptanceより前、xbuildがacceptanceより後である順序を検査。
- staleなGrouping-only末尾がbuild scriptへ再混入しないことを検査。

## 非変更範囲

滑走路A/B絶対座標、測地投影、手動校正、RWY番号、AIRFIELDS grouping、ND、LAND、Terrain、Auto Preload、AP、AA、PROTECT、APPの動作ロジックは変更していない。
