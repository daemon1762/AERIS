#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=root/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
s=p.read_text(encoding='utf-8')
old='const int MaximumPooledMeshes = 96;'
new='const int MaximumPooledMeshes = 24;'
if old not in s: raise SystemExit('mesh pool cap anchor not found')
s=s.replace(old,new,1)
p.write_text(s,encoding='utf-8')
print('Operation Health Pass 2 bounded mesh pool finalized')
