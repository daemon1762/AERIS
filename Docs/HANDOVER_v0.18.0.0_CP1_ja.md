# AERIS v0.18.0.0 DEV CP1 引き継ぎ

## 完了

- Gate 0 Physical Runway Canonical Federation
- Gate 1 ND計測・高低頻度レイヤー分離
- Gate 2 fixed range、TRACK/NORTH、PLAN/RECENTER、runway常時表示、Preview/SELECT/CENTER

## 再開位置

ユーザー実機試験でCP1を判定する。compile/runtime問題があればCP1の範囲内で修正する。CP1合格後のみCP2へ進み、Terrain Tile/LOD/三層cache/GPU terrainを実装する。

## 絶対条件

- AP/BANKへ変更を入れない
- 旧NAVを戻さない
- 新NAVへ着手しない
- LANDへ飛行制御権限を与えない
- ND専用ThreadPoolを作らない
- GPU結果を安全判定の唯一の根拠にしない

## 未実施

- assistant環境でのxbuild
- KSP起動・実描画
- user操作によるPLAN/SELECT検証
- performance CSV実測
- scene/vessel/body遷移実測

## 主要ソース

- `Performance/AERISNavigationDisplayPipeline.cs`
- `Performance/AERISWorkerScheduler.cs`
- `Performance/AERISPerformanceRuntime.cs`
- `Terrain/AERISTerrainPerformance.cs`
- `UI/AERISNavigationDisplay.cs`
- `Settings/AERISSettings.cs`
