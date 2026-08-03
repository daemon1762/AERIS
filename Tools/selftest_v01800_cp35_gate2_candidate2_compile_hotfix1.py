#!/usr/bin/env python3
import sys,re
sys.dont_write_bytecode=True
from pathlib import Path
from v01700_testlib import ROOT,CheckSuite,read
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 2 Candidate 2 Compile Hotfix 1')
renderer=read(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
version=read(ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
readme=read(ROOT/'README.md')

suite.check('static bool DrawPreparedEntry(' not in renderer,
            'DrawPreparedEntry is not static')
suite.check(re.search(r'\n\s*bool DrawPreparedEntry\(Entry entry, Matrix4x4 mapMatrix,',renderer) is not None,
            'DrawPreparedEntry is an instance method')
suite.check('bool entryRendered = DrawPreparedEntry(entry, batch.MapRotation, true,' in renderer,
            'instance RenderPreparedBackBuffer still calls DrawPreparedEntry')
for field in ('terrainMaterial','contourMaterial','coastlineMaterial'):
    suite.check(field+'.SetPass(0)' in renderer,field+' draw path retained')
suite.check('entry.WaterMesh != null && terrainMaterial.SetPass(0)' in renderer,
            'water draw path unchanged')
suite.check('entry.LandMesh != null && terrainMaterial.SetPass(0)' in renderer,
            'land draw path unchanged')
suite.check('drawContours && entry.ContourMesh != null && contourMaterial.SetPass(0)' in renderer,
            'contour draw path unchanged')
suite.check('entry.CoastlineMesh != null && coastlineMaterial.SetPass(0)' in renderer,
            'coastline draw path unchanged')
identity='DEV CP3.5 GATE 2 — EXACT KEYFRAME / OVERSCAN TEMPORAL REPROJECTION / MULTICORE GPU CANDIDATE 2 — COMPILE HOTFIX 1'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,
            'generated source UiCheckpoint identifies Compile Hotfix 1')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,
            'build generator emits Compile Hotfix 1 UiCheckpoint')
suite.check('CANDIDATE 2 COMPILE HOTFIX 1 / DEV CP3.5 GATE 2 EXACT KEYFRAME' in build,
            'build Display preserves hotfix and base lineage')
suite.check('Candidate 2 Compile Hotfix 1 / AERISFlightControl DEV CP3.5 Gate 2' in avc,
            'AVC metadata identifies Compile Hotfix 1 and base lineage')
suite.check('CS0120' in readme and 'DrawPreparedEntry' in readme,
            'README records compiler failure and repair')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE2_EXACT_KEYFRAME_OVERSCAN_TEMPORAL_REPROJECTION_MULTICORE_GPU_CANDIDATE2_COMPILE_HOTFIX1.txt').is_file(),
            'hotfix acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE2_CANDIDATE2_COMPILE_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'hotfix test card included')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3.5_GATE2_CANDIDATE2_COMPILE_HOTFIX1.txt').is_file(),
            'source diff audit included')
suite.finish()
