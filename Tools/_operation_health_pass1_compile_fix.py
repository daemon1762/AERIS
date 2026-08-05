#!/usr/bin/env python3
from pathlib import Path
p=Path('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
s=p.read_text(encoding='utf-8')
old='''                Entry fallbackEntry, currentEntry;
                ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
                    out currentEntry);'''
new='''                Entry fallbackEntry, currentEntry;
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                ResolveRenderableEntries(tile, cacheKey, styleKey, out fallbackEntry,
                    out currentEntry);'''
count=s.count(old)
if count != 1:
    raise SystemExit('coverage ResolveRenderableEntries old call expected 1, found %d' % count)
s=s.replace(old,new,1)
p.write_text(s,encoding='utf-8')
print('Operation Health Pass 1 compile fix applied')
