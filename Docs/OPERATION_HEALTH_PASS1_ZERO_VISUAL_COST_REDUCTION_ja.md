# Operation Health — Pass 1: Zero-Visual-Cost Reduction

## 目的
Candidate11 の描画結果を品質下限として完全維持したまま、ND terrain renderer のCPU/GC管理コストを削減する。

## 絶対条件
- 地形解像度を下げない。
- coastline 129x129 authority を変更しない。
- Sparse coastal fill を変更しない。
- contour interval / contour level budget / coastal clip を変更しない。
- RenderTexture解像度・filter・描画順序を変更しない。
- FRONT/BACK authority、projection threshold、refresh cadenceを変更しない。
- AP/FBW/PROTECTその他non-NDを変更しない。

## Pass 1対象
1. GPU EntryをTileKey別に索引化し、ResolveRenderableEntriesの全entry走査を廃止。
2. 1 repaint中にresolveしたcurrent/fallback/draw entryをscratch arrayへ保持し、readiness/renderで再resolveしない。
3. visible.Tiles.Clone()をreusable exact-length scratch arrayへ置換。
4. CacheKeyは1 tile / repaintにつき一度だけ生成し、upload/schedule/resolveへ受け渡す。
5. pending marker文字列 `cacheKey + "|PENDING"` を廃止し、専用HashSetで同一frame schedule重複を防止。
6. projection center latitude/longitudeを1 renderにつき一度だけ計算し、全entryへ共有。
7. Operation Health telemetryを追加し、resolve candidate走査量とscratch再確保回数を観測可能にする。

## 非対象
- update rate変更
- LOD変更
- contour thinning追加
- coastline simplification
- mesh topology変更
- shader変更
- history reprojection再有効化

## 成功条件
- Candidate11と目視差ゼロ。
- 10/20/40/80/160kmで海岸・fill・contour・REL/TOPO色・runway map-lockに回帰なし。
- runtime logでEntry resolve candidate走査量が全entry走査方式より大幅減。
- frame pacing/FPSが同等以上。
