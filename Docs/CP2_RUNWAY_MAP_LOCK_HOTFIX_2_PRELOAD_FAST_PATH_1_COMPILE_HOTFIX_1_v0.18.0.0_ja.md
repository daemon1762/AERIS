# CP2 Runway Map Lock Hotfix 2 + Preload Fast Path 1 Compile Hotfix 1

## 修正

`Terrain/AERISNdMapProjection.cs` が `AERISTerrainRenderTargetOrientation` を使用しているにもかかわらず、型を定義する `AERISFlightControl.Settings` 名前空間を参照していなかったため、Mono/xbuildでCS0246になっていた。

```csharp
using AERISFlightControl.Settings;
```

を追加した。

## 変更境界

- 投影数式、地形描画、滑走路Map Lock、Preload Fast Pathの挙動変更なし
- AP/BANK/HDG/PITCH/V/S/ALT/ACC/VEL/Ground Stability変更なし
- コンパイル参照修正と専用静的回帰試験のみ
