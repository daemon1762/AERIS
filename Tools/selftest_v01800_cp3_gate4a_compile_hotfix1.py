#!/usr/bin/env python3
from pathlib import Path
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A Compile Hotfix 1")
source = ROOT / "Source/AERISFlightControl"
csproj = read(source / "AERISFlightControl.csproj")
awareness = read(source / "Terrain/AERISTerrainAwareness.cs")
retired = read(source / "Terrain/AERISTerrainRasterWorker.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(source / "Properties/AERISBuildVersion.generated.cs")
readme = read(ROOT / "README.md")
avc = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")

suite.check('Terrain\\AERISTerrainRasterWorker.cs' not in csproj,
            "retired CPU raster worker remains excluded from csproj")
suite.check("RETIRED IN CP3 GATE 4A" in retired,
            "retired raster worker remains audit-only tombstone")
suite.check("AERISTerrainGridSnapshot" not in awareness,
            "compiled awareness has no retired grid snapshot type reference")
suite.check("TryCaptureGridSnapshot" not in awareness,
            "compiled awareness has no retired grid snapshot method")

# Parse every Compile Include and ensure the retired CPU raster contracts do not leak into compiled sources.
includes = re.findall(r'<Compile Include="([^"]+\.cs)"\s*/>', csproj)
compiled = []
missing = []
for rel in includes:
    path = source / rel.replace('\\','/')
    if not path.is_file():
        missing.append(rel)
        continue
    compiled.append((rel, path.read_text(encoding='utf-8')))
suite.check(not missing, "all csproj Compile Include files exist", ", ".join(missing[:10]))
for token in ("AERISTerrainGridSnapshot", "AERISTerrainRasterResult", "AERISTerrainRasterWorker"):
    offenders = [rel for rel,text in compiled if token in text]
    suite.check(not offenders, "compiled source excludes retired contract " + token,
                ", ".join(offenders[:10]))

expected_ui = 'UiCheckpoint = "DEV CP3 GATE 4A — RENDER-READY HEIGHT FIELD & GPU-ONLY FAR PRESENTATION — COMPILE HOTFIX 1"'
suite.check(expected_ui in version,
            "generated tab label identifies Gate 4A Compile Hotfix 1")
suite.check(expected_ui in build,
            "build entrypoint regenerates exact Gate 4A Compile Hotfix 1 tab label")
suite.check("Gate 4A — Render-Ready Height Field & GPU-Only FAR Presentation — Compile Hotfix 1" in readme,
            "README identifies current Gate 4A Compile Hotfix 1 checkpoint")
suite.check("Gate 4A Render-Ready Height Field & GPU-Only FAR Presentation Compile Hotfix 1" in avc,
            "AVC identity identifies Gate 4A Compile Hotfix 1")
suite.check("CPU SAFETY FALLBACK" not in version,
            "current version label does not restore CPU safety fallback")
suite.check((ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION_COMPILE_HOTFIX1.txt").is_file(),
            "compile hotfix acceptance contract exists")
suite.finish()
