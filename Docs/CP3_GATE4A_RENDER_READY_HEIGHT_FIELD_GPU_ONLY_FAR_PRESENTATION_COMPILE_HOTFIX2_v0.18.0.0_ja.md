# CP3 Gate 4A Compile Hotfix 2

## 修正対象
Gate 4A Compile Hotfix 1では`AERISTerrainGpuTileRenderer.Draw()`から`AutomaticGpuCapabilityAvailable()`を呼んでいたが、Gate 4A renderer再編時にメソッド本体を落としていたためMono/xbuildでCS0103になった。

## 修正
Gate 3.1で使用していたGPU自動判定を同じ条件で復元する。

```text
RenderTexture support
ARGB32 RenderTexture support
graphics shader level >= 20
```

CPU terrain描画、CPU safety fallback、retired raster workerは復活させない。GPU FRONT/BACK、FAR 100%完成後swap、Render-Ready Height Fieldの契約は変更しない。
