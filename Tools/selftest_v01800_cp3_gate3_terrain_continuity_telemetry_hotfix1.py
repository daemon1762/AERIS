#!/usr/bin/env python3
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 3 Terrain Continuity Telemetry Hotfix 1")
renderer_path = SOURCE / "Terrain/AERISTerrainGpuTileRenderer.cs"
nd_path = SOURCE / "UI/AERISNavigationDisplay.cs"
performance_path = SOURCE / "Terrain/AERISTerrainPerformance.cs"
awareness_path = SOURCE / "Terrain/AERISTerrainAwareness.cs"
runtime_path = SOURCE / "Performance/AERISPerformanceRuntime.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
build_path = ROOT / "build_ubuntu.sh"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
bootstrap_path = SOURCE / "Core/AERISBootstrap.cs"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE3_TERRAIN_CONTINUITY_TELEMETRY_HOTFIX1.txt"
spec_path = ROOT / "Docs/CP3_GATE3_TERRAIN_CONTINUITY_TELEMETRY_HOTFIX_1_v0.18.0.0_ja.md"
card_path = ROOT / "Docs/ND_CP3_GATE3_TERRAIN_CONTINUITY_TELEMETRY_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md"
runner_path = ROOT / "Tools/run_v01800_cp3_gate3_terrain_continuity_telemetry_hotfix1_acceptance.py"

renderer = read(renderer_path)
nd = read(nd_path)
performance = read(performance_path)
awareness = read(awareness_path)
runtime = read(runtime_path)
tiles = read(tiles_path)
generated = read(generated_path)
build = read(build_path)
version = read(version_path)
bootstrap = read(bootstrap_path)
acceptance = read(acceptance_path)
spec = read(spec_path)
card = read(card_path)
runner = read(runner_path)

for name, text in (("GPU renderer", renderer), ("navigation display", nd),
                   ("terrain performance", performance),
                   ("terrain awareness", awareness),
                   ("performance runtime", runtime), ("terrain tile system", tiles)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " C# braces are balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " C# parentheses are balanced")

# Partial-render continuity and transparent composition.
suite.check("RenderTexture continuityTarget" in renderer and
            'continuityTarget.name = "AERIS_ND_TERRAIN_CONTINUITY"' in renderer,
            "a dedicated continuity RenderTexture exists")
suite.check("continuityTargetBytes" in renderer and
            "Math.Max(0L, continuityTargetBytes)" in renderer,
            "continuity RenderTexture is included in VRAM accounting")
suite.check("TrySeedContinuityFrame" in renderer and
            "CommitContinuityFrame" in renderer and
            "ResetContinuityState" in renderer,
            "complete-frame continuity lifecycle is explicit")
suite.check("ageSeconds > 15f" in renderer and
            "ratio < 0.80f || ratio > 1.25f" in renderer,
            "continuity reuse has bounded age and range compatibility")
suite.check("Math.Max(1500.0, safeRange * 0.20)" in renderer and
            "> 18f" in renderer,
            "continuity reuse rejects excessive movement and track rotation")
suite.check("continuityBodyRadiusMillimetres" in renderer and
            "continuityOrientation != orientation" in renderer and
            "continuityTrackUp != trackUp" in renderer,
            "continuity ownership includes body radius and render orientation")
suite.check("Graphics.Blit(continuityTarget, renderTarget)" in renderer and
            "Graphics.Blit(renderTarget, continuityTarget)" in renderer,
            "continuity frame can seed and receive complete commits")
suite.check("callerFallbackAvailable" in renderer and
            "useUnknownBacking = !lastContinuitySeeded && !callerFallbackAvailable" in renderer,
            "GPU composition distinguishes CPU fallback from unknown backing")
suite.check("new Color(0.025f, 0.080f, 0.120f, 1f)" in renderer and
            "Color.black" not in renderer[renderer.find("Color unknownBacking"):
                                           renderer.find("Stopwatch frameWatch")],
            "no-fallback partial view uses explicit non-black unknown-terrain backing")
suite.check("GUI.DrawTextureWithTexCoords(plot, renderTarget, uv, true);" in renderer,
            "partial RenderTexture uses alpha blending so CPU/background remains visible")
suite.check("GL.Clear(true, !lastContinuitySeeded" in renderer,
            "seeded continuity is not erased before progressive tile overlay")
suite.check("lastDrawState == AERISTerrainGpuDrawState.Complete" in renderer and
            "CommitContinuityFrame" in renderer[renderer.find(
                "lastDrawState = lastCoverageFraction"):renderer.find(
                "bool TrySeedContinuityFrame")],
            "only complete requested-quality frames become continuity authority")
suite.check("[CP3_TERRAIN_CONTINUITY]" in renderer and
            "COMMITTED_FRAME" in renderer and "CPU_FALLBACK" in renderer and
            "UNKNOWN_TERRAIN" in renderer,
            "partial backing source is persisted to periodic logs")
suite.check("const int samplesPerAxis = 25;" in renderer and
            "row < samplesPerAxis" in renderer and
            "column < samplesPerAxis" in renderer,
            "viewport coverage sampling is strengthened to 25x25")
suite.check("DestroyRenderTexture(ref continuityTarget)" in renderer and
            "ResetContinuityState();" in renderer[renderer.find(
                "void DestroyRenderTargets"):renderer.find(
                "static void DestroyRenderTexture")],
            "continuity GPU resources are released on renderer teardown")

# Latest-only manual range control.
suite.check("RangeChangeDebounceSeconds = 0.35f" in nd,
            "manual ND range changes use a 350ms debounce")
suite.check("pendingManualRangeMeters" in nd and
            "pendingManualRangeApplyRealtime" in nd and
            "PendingOrCurrentRange" in nd,
            "range UI tracks the latest pending value")
suite.check("FlushPendingManualRange();" in nd and
            "Time.realtimeSinceStartup < pendingManualRangeApplyRealtime" in nd,
            "pending range is committed only after the coalescing interval")
suite.check("terrainTileRenderer.InvalidatePendingForViewChange();" in nd and
            "rasterizer.CancelAll();" in renderer,
            "range commit cancels obsolete GPU mesh preparation")
suite.check('"m (coalesced)"' in nd,
            "range commits are identifiable in logs")
suite.check("pendingManualRangeMeters = float.NaN" in nd,
            "pending range state is explicitly cleared after commit/reset")

# Airfield startup work must not degrade terrain AUTO policy and Landing remains frozen.
suite.check("SetExternalMaintenanceActive" in performance and
            "maintenanceHoldUntilRealtime = Time.realtimeSinceStartup + 5f" in performance,
            "terrain AUTO adaptation has a five-second post-maintenance hold")
suite.check("if (ExternalMaintenanceHold)" in performance and
            "overloadSeconds = 0f" in performance and
            "workerBacklogged = false" in performance,
            "external maintenance cannot accumulate terrain overload state")
suite.check("AUTO quality/rate hold:" in performance and
            "airfield maintenance isolated from terrain adaptation" in performance and
            "AUTO adaptation resumed after airfield maintenance" in performance,
            "maintenance isolation has start/resume diagnostics")
for state in ("LoadingCache", "Discovering", "Surveying", "Validating", "Staged"):
    suite.check("AERISAirfieldReloadState." + state in awareness,
                "airfield maintenance state is recognized outside Landing: " + state)
suite.check("airfields.ReloadActive" not in awareness,
            "hotfix does not require a new Landing owner API")

# Persistent CP3 telemetry.
for header in ("cp3_resident_ram_bytes", "cp3_resident_global_count",
               "cp3_resident_decode_submissions", "cp3_corridor_ground_speed_mps",
               "cp3_corridor_requested_tiles", "cp3_corridor_land_demand",
               "terrain_continuity_seeded", "terrain_continuity_reuse_frames",
               "terrain_unknown_backing_frames", "terrain_continuity_age_ms"):
    suite.check('"' + header + '"' in runtime,
                "performance CSV includes " + header)
suite.check("RecordCp3ResidentCorridorState" in runtime and
            "RecordTerrainContinuityState" in runtime,
            "runtime exposes CP3 resident/corridor/continuity recorders")
suite.check("currentBodyResidentCache.SnapshotTelemetry()" in tiles and
            "predictiveCorridor.Snapshot()" in tiles and
            "RecordCp3ResidentCorridorState" in tiles,
            "Resident and corridor snapshots feed persistent runtime telemetry")
suite.check("nextCp3TelemetrySampleRealtime = now + 1f" in tiles and
            tiles.find("currentBodyResidentCache.SnapshotTelemetry()") >
            tiles.find("if (now >= nextCp3TelemetrySampleRealtime)"),
            "O(n) Resident telemetry snapshot is rate-limited to one hertz")
suite.check("nextCp3TelemetryLogRealtime = now + 10f" in tiles and
            "[CP3_TELEMETRY]" in tiles,
            "human-readable CP3 state is logged every ten seconds")
suite.check("terrainTileRenderer.LastContinuitySeeded" in nd and
            "terrainTileRenderer.ContinuityReuseFrames" in nd and
            "terrainTileRenderer.UnknownBackingFrames" in nd,
            "ND publishes continuity counters into performance telemetry")

# Header/value cardinality guard for the long performance CSV.
def extract_call(text, marker, start_at=0):
    i = text.index(marker, start_at)
    i = text.index("(", i) + 1
    depth = 1
    in_string = False
    escaped = False
    j = i
    while j < len(text):
        c = text[j]
        if in_string:
            if escaped: escaped = False
            elif c == "\\": escaped = True
            elif c == '"': in_string = False
        else:
            if c == '"': in_string = True
            elif c == "(": depth += 1
            elif c == ")":
                depth -= 1
                if depth == 0: return text[i:j]
        j += 1
    raise ValueError(marker)

def split_top_level(text):
    output = []
    start = 0
    depths = {"(": 0, "[": 0, "{": 0}
    reverse = {")": "(", "]": "[", "}": "{"}
    in_string = False
    escaped = False
    for i, c in enumerate(text):
        if in_string:
            if escaped: escaped = False
            elif c == "\\": escaped = True
            elif c == '"': in_string = False
            continue
        if c == '"': in_string = True
        elif c in depths: depths[c] += 1
        elif c in reverse: depths[reverse[c]] -= 1
        elif c == "," and not any(depths.values()):
            output.append(text[start:i].strip())
            start = i + 1
    if text[start:].strip(): output.append(text[start:].strip())
    return output

try:
    header_call = extract_call(runtime, "telemetryWriter.WriteHeader")
    header_arg = split_top_level(header_call)[0]
    headers = split_top_level(header_arg[header_arg.index("{") + 1:header_arg.rindex("}")])
    value_call = extract_call(runtime, "telemetryWriter.WriteCsv",
                              runtime.index("void WriteTelemetry"))
    value_arg = split_top_level(value_call)[0]
    values = split_top_level(value_arg[value_arg.index("{") + 1:value_arg.rindex("}")])
    suite.check(len(headers) == len(values) and len(headers) >= 180,
                "performance CSV header/value cardinality remains exact")
except Exception:
    suite.check(False, "performance CSV header/value cardinality remains exact")

# Build identity and active runner: this prevents the reported stale tab label regression.
ui_label = 'UiCheckpoint = "DEV CP3 GATE 3 — TERRAIN CONTINUITY & TELEMETRY HOTFIX 1"'
suite.check(ui_label in generated and ui_label in build,
            "generated and build-time tab labels name Hotfix 1")
suite.check("DEV CP3 GATE 3 TERRAIN CONTINUITY TELEMETRY HOTFIX 1" in generated and
            "DEV CP3 GATE 3 TERRAIN CONTINUITY TELEMETRY HOTFIX 1" in build,
            "assembly/build display identity names Hotfix 1")
suite.check("CP3 Gate 3 Terrain Continuity Telemetry Hotfix 1" in version,
            "AVC package identity names Hotfix 1")
suite.check("Terrain Continuity and Telemetry Hotfix 1" in bootstrap and
            "CP3 resident/corridor/continuity telemetry" in bootstrap,
            "startup log describes the hotfix routes")
suite.check("run_v01800_cp3_gate3_terrain_continuity_telemetry_hotfix1_acceptance.py" in build,
            "build entrypoint invokes the active Hotfix 1 runner")
suite.check("selftest_v01800_cp3_gate3_terrain_continuity_telemetry_hotfix1.py" in runner,
            "active runner invokes the dedicated Hotfix 1 test")
suite.check("BLACK WEDGE" in acceptance and "NATIVE COMPILE REQUIRED" in acceptance,
            "acceptance contract records continuity failure and native-build boundary")
suite.check("350ms" in spec and "15秒" in spec and "25×25" in spec and
            "1Hz" in spec and "10秒" in spec,
            "Japanese specification records all new timing and sampling bounds")
suite.check("黒い扇形" in card and "CP3_TELEMETRY" in card and
            "CP3_TERRAIN_CONTINUITY" in card,
            "runtime test card covers visual continuity and telemetry logs")

# Frozen control, LAND and runway ownership boundaries.
def tree_hash(rel):
    base = ROOT / rel
    h = hashlib.sha256()
    for path in sorted(p for p in base.rglob("*") if p.is_file()):
        h.update(path.relative_to(ROOT).as_posix().encode("utf-8") + b"\0")
        h.update(hashlib.sha256(path.read_bytes()).digest())
    return h.hexdigest()

protected = {
    "Source/AERISFlightControl/AA": "79f241cf024a81851dd11e41f2ae38485554c6375318d8201b90383fbdbc1726",
    "Source/AERISFlightControl/Autopilot": "49557b5408e8e5a9b406ac1f42b06abe6ea49c29e6f96d6f2d4ad880721e544c",
    "Source/AERISFlightControl/Protect": "8d5a103421b2c88fc9f7c20414e87250dd00b265df2331adbfcd666001135438",
    "Source/AERISFlightControl/FlightState": "cb7cc694b8cee6935797b8050368a7e061249883d7a664483676678940cc080a",
    "Source/AERISFlightControl/Landing": "d99906b21e57e70e3d7ec4a592524d773ebd3a3a7a08c300076ba0395220abbe",
    "Source/AERISFlightControl/Integrations": "43cac3baf7f5455d61feb26a5ce488336936a81a4e573d4d48a44a5bdb4c7efc",
    "GameData/AERISFlightControl/Airfields": "f9c83c5877a3c3234eee4057cae2f43935634a837d500f5d2636dce1ec593ac9",
}
for rel, expected in protected.items():
    suite.check(tree_hash(rel) == expected,
                "protected tree remains byte-identical: " + rel)

map_path = SOURCE / "Performance/AERISMapDramCache.cs"
suite.check(hashlib.sha256(map_path.read_bytes()).hexdigest() ==
            "32f69a41ef84a6ecef280921fcd5ae9f13d729eba7a080ef53fee644c24679e5",
            "Map DRAM implementation remains byte-identical and metadata-only")
all_cs = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "[PRELOAD_BOOST]",
              "AERISRuntimeLane.SafetyLand"):
    if token == "AERISRuntimeLane.SafetyLand":
        hotfix_sections = renderer + nd + performance + awareness + runtime + tiles
        suite.check(token not in hotfix_sections,
                    "hotfix adds no Flight safety-lane work")
    else:
        suite.check(token not in all_cs, "FULL BOOST remains absent: " + token)

suite.finish()
