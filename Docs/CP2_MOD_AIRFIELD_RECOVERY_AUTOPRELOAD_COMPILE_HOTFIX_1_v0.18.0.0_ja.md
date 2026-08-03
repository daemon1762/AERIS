# CP2 Mod Airfield Recovery / Auto Preload Progression Compile Hotfix 1

## 原因

`AERISTerrainPreloadBuilder.cs`へ追加した自動進行ログ2箇所が、実在しない`AERISLog.Info`を参照していた。AERISの実装クラスは`AERISFlightControl.Logging.AERISLogger`であるため、Mono/xbuildでCS0103となった。

## 修正

```csharp
AERISLog.Info(...)
```

を2箇所とも、既存ロガーの

```csharp
AERISLogger.Info(...)
```

へ置換した。既に`using AERISFlightControl.Logging;`が存在するため、追加依存はない。

## 影響範囲

- 自動Preloadの`COMPLETE`ログ
- 自動Preloadの`PROMOTE`ログ
- Build identityおよび再発防止静的試験

空港認証条件、自動天体進行、PQS予算、DB形式、Preload Fast Path、ND描画、LAND、操縦系の動作は変更していない。
