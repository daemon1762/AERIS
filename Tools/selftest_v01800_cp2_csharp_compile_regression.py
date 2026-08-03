#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 C# definite-assignment compile regression")
terrain_path = SOURCE / "Terrain" / "AERISTerrainTileSystem.cs"
nav_path = SOURCE / "UI" / "AERISNavigationDisplay.cs"
suite.check(terrain_path.is_file(), "terrain tile system source exists")
suite.check(nav_path.is_file(), "navigation display source exists")
terrain = read(terrain_path)
nav = read(nav_path)
retry_start = terrain.index("void RetryPendingDiskWrites(float now)")
retry_end = terrain.index("void TrySubmitDiskWrite(string id, float now)", retry_start)
retry = terrain[retry_start:retry_end]
draw_start = nav.index("void DrawLocal(Rect rect, float scale)")
draw_end = nav.index("void UpdateRateLimitedSnapshots", draw_start)
draw = nav[draw_start:draw_end]

suite.check("int available = 0;" in retry,
            "disk-write availability is initialized before the synchronization scope")
suite.check(retry.find("int available = 0;") < retry.find("lock (sync)"),
            "disk-write availability declaration precedes lock scope")
suite.check("int writeLimit = ResolveWriteIoLimitLocked(limit);" in retry and
            "available = Math.Max(0, writeLimit - diskWriting.Count);" in retry,
            "disk-write availability is assigned from the read-aware limit inside lock scope")
suite.check("int ResolveWriteIoLimitLocked(int totalIoLimit)" in terrain and
            "return Math.Max(0, limit - 1);" in terrain,
            "disk writes reserve the last I/O slot while reads are pending")
suite.check("int available = Math.Max" not in retry,
            "disk-write availability is not block-scoped inside lock")
suite.check("Math.Min(available, diskWriteReadyScratch.Count)" in retry,
            "initialized availability is consumed after lock scope")

suite.check("AERISPreparedTrafficFrame trafficFrame = null;" in draw,
            "traffic frame is initialized before short-circuit expression")
suite.check("settings.NavigationDisplayTrafficEnabled &&" in draw and
            "TryGetLatest(out trafficFrame)" in draw,
            "traffic frame short-circuit acquisition remains intact")
suite.check("if (!hasTrafficFrame) trafficFrame = null;" in draw,
            "invalid traffic frame is explicitly cleared")
suite.check("HandleMapInteraction(plan, frame, trafficFrame" in draw,
            "definitely assigned traffic frame reaches map interaction")



# Hotfix 3 changed the renderer return contract and added preview/final request state.
# These checks guard the definite-assignment and return-type boundaries that cannot be
# natively compiled in the assistant environment.
renderer = read(SOURCE / "Terrain" / "AERISTerrainGpuTileRenderer.cs")
contracts = read(SOURCE / "Terrain" / "AERISTerrainTileContracts.cs")
rasterizer = read(SOURCE / "Terrain" / "AERISTerrainGpuTileRasterizer.cs")
cache = read(SOURCE / "Terrain" / "AERISTerrainTileCache.cs")

suite.check("internal AERISTerrainGpuDrawState Draw(Rect plot" in renderer,
            "GPU Draw has the explicit progressive draw-state return type")
suite.check("AERISTerrainGpuDrawState gpuState = AERISTerrainGpuDrawState.None;" in nav,
            "navigation display initializes progressive draw state")
suite.check("gpuState = terrainTileRenderer.Draw(plot" in nav,
            "navigation display assigns renderer result before use")
suite.check("return lastDrawState;" in renderer,
            "every progressive renderer exit returns the initialized state field")
suite.check("internal enum AERISTerrainGpuDrawState" in contracts,
            "progressive draw-state enum is compiled in shared terrain contracts")
suite.check("double finalIntervals = Math.Max(1," in rasterizer and
            "double actualIntervals = Math.Max(1, resolution - 1);" in rasterizer,
            "preview slope interval variables are assigned before use")
suite.check("internal int CountPreviewTiles(ICollection<string> stableIds)" in cache,
            "RAM preview telemetry accepts the already imported generic collection contract")



# Compile Hotfix 1: every AERISTerrainDisplayMode member reference must exist
# in the actual enum. This catches misspellings such as `.Auto` that brace and
# definite-assignment checks cannot detect.
import re
enum_match = re.search(r"internal\s+enum\s+AERISTerrainDisplayMode\s*\{([^}]*)\}", contracts, re.S)
suite.check(enum_match is not None, "terrain display mode enum can be parsed")
enum_members = set()
if enum_match is not None:
    for item in enum_match.group(1).split(','):
        name = item.split('=', 1)[0].strip()
        if name:
            enum_members.add(name)
all_cs = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
mode_refs = set(re.findall(r"AERISTerrainDisplayMode\.([A-Za-z_][A-Za-z0-9_]*)", all_cs))
unknown_mode_refs = sorted(mode_refs - enum_members)
suite.check(not unknown_mode_refs,
            "all terrain display mode references resolve to declared enum members",
            ", ".join(unknown_mode_refs))
suite.check("Automatic" in mode_refs and "Auto" not in mode_refs,
            "terrain request generation uses Automatic, never the nonexistent Auto member")

# Generic Runway Placement Final Candidate 3 Compile Hotfix 1:
# `stored` must be assigned even when witnessLibrary == null short-circuits
# before RecordPlacementMismatch(out stored) executes.
airfield_registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
verify_start = airfield_registry.index("internal bool VerifyRunwayPlacement")
verify_end = airfield_registry.index("static double InitialBearing", verify_start)
verify_method = airfield_registry[verify_start:verify_end]
suite.check("string stored = string.Empty;" in verify_method,
            "runway placement quarantine detail is initialized before short-circuit")
suite.check(verify_method.find("string stored = string.Empty;") <
            verify_method.find("witnessLibrary == null ||"),
            "runway placement stored initialization precedes witness-library null gate")
suite.check("string stored;" not in verify_method,
            "uninitialized runway placement quarantine detail cannot regress")

suite.finish()
