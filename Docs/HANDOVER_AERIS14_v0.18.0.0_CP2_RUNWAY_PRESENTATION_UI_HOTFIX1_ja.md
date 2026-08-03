# AERIS14 引き継ぎ — CP2 Runway Presentation UI Hotfix 1

## 現在地

CP2はOPEN。Runway Map Lock Hotfix 2により旋回中の相対滑りは解消したが、範囲外滑走路のエッジ表示が地図中央基準だったため、一部空港で滑走路が海上などへ固定表示されるように見える問題が残った。

## 根本原因

TRACK UPでは自機を地図高さ75%位置へ置く。一方、旧`DrawSelectedRunwayEdgePointer`は`plot.center`を起点に外周交点を求めていた。このため滑走路距離がRangeを超える場合、実滑走路ではないポインターが地図内部の誤った位置へ描かれた。

KSC接地ログでは機体位置と登録中心線の横ずれは約13mであり、KSC登録座標の大幅な誤りは確認されていない。

## 実装

- エッジポインター起点を自機Map Anchorへ統一。
- 正のray-box交点だけを採用し、外周内側へclamp。
- ポインターを滑走路線と区別できる矢印表示へ限定。
- 滑走路名と現在距離を表示。
- 上部`CLR SEL`を廃止。
- 下段を`ARM / CENTER / APP xx / SELECT / CLEAR`へ再配置。
- `APP xx`は同一滑走路の二方向だけを切替。
- 狭いパネルでは`ARM / CTR / RWYxx / SEL / CLR`へ短縮。
- CLEARはLAND DISARM、Registry選択解除、ND cache破棄を一括実行。

## 凍結

AP、BANK、HDG、PITCH、V/S、ALT、ACC、VEL、Ground Stabilityは変更しない。Preload Fast Path 1と共通投影も維持する。

## 次の判定

範囲外KolaIsland、範囲内KSCまたはIsland Airfield、Range連続変更、90度以上旋回、UI縮小を録画する。全PASS後にCP2 CLOSE可否を再判定する。
