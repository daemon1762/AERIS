#!/usr/bin/env python3
from pathlib import Path
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A Compile Hotfix 2")
source = ROOT / "Source/AERISFlightControl"
renderer = read(source / "Terrain/AERISTerrainGpuTileRenderer.cs")
csproj = read(source / "AERISFlightControl.csproj")
build = read(ROOT / "build_ubuntu.sh")
version = read(source / "Properties/AERISBuildVersion.generated.cs")
readme = read(ROOT / "README.md")
avc = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")

suite.check(renderer.count("AutomaticGpuCapabilityAvailable()") == 2,
            "automatic GPU capability contract has one call and one declaration")
suite.check("static bool AutomaticGpuCapabilityAvailable()" in renderer,
            "automatic GPU capability method exists")
suite.check("SystemInfo.supportsRenderTextures" in renderer,
            "automatic GPU capability requires RenderTexture support")
suite.check("SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32)" in renderer,
            "automatic GPU capability requires ARGB32 RenderTexture")
suite.check("SystemInfo.graphicsShaderLevel >= 20" in renderer,
            "automatic GPU capability requires shader model capability")
suite.check("Terrain\\AERISTerrainRasterWorker.cs" not in csproj,
            "CPU raster worker remains excluded")
suite.check("CPU SAFETY FALLBACK" not in renderer,
            "GPU capability fix does not restore CPU safety fallback")
expected_ui = 'UiCheckpoint = "DEV CP3 GATE 4A — RENDER-READY HEIGHT FIELD & GPU-ONLY FAR PRESENTATION — COMPILE HOTFIX 2"'
suite.check(expected_ui in version, "generated tab label identifies Compile Hotfix 2")
suite.check(expected_ui in build, "build entrypoint regenerates Compile Hotfix 2 label")
suite.check("Gate 4A — Render-Ready Height Field & GPU-Only FAR Presentation — Compile Hotfix 2" in readme,
            "README identifies Compile Hotfix 2")
suite.check("Gate 4A Render-Ready Height Field & GPU-Only FAR Presentation Compile Hotfix 2" in avc,
            "AVC identity identifies Compile Hotfix 2")
suite.check((ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION_COMPILE_HOTFIX2.txt").is_file(),
            "Compile Hotfix 2 acceptance contract exists")
suite.finish()
