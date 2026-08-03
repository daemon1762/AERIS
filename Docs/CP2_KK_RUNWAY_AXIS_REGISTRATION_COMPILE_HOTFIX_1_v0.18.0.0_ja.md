# CP2 KK Runway Axis Registration Compile Hotfix 1

## 原因

Mono/xbuild が使用するC#コンパイラでは、`TryRunwaySurfacePca`内の子スコープで宣言した`aspect`と、その後の親スコープで宣言した`aspect`がCS0136となる。

## 修正

- Primitive評価値：`primitiveAspect`
- 最終PCA評価値：`surfaceAspect`

へ分離した。数式、閾値、軸推定、認証条件は変更していない。

## 影響範囲

`Landing/AERISRunwayGeometryWorker.cs`の変数名だけ。Preload、ND投影、LAND表示、操縦系には動作変更なし。
