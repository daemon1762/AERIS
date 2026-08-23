#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
BASE_OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
FIX1_OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

FIX1 = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
MARKER = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'


def replace_once(text, old, new, label):
    if old not in text:
        raise SystemExit('R030 Fix2 anchor missing: ' + label)
    if text.count(old) != 1:
        raise SystemExit('R030 Fix2 anchor not unique: ' + label + ' count=' + str(text.count(old)))
    return text.replace(old, new, 1)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def tree_hash():
    h = hashlib.sha256()
    files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs'))
    files += [CSPROJ]
    for path in files:
        if path == VERSION:
            continue
        h.update(str(path.relative_to(ROOT)).encode())
        h.update(b'\0')
        h.update(path.read_bytes())
        h.update(b'\0')
    return h.hexdigest()


for required in (BUILDER, BASE_OBSERVER, FIX1_OBSERVER, CSPROJ, VERSION):
    if not required.is_file():
        raise SystemExit('R030 Fix2 required file missing: ' + str(required))

builder = BUILDER.read_text()
base = BASE_OBSERVER.read_text()
if FIX1 not in builder or FIX1 not in FIX1_OBSERVER.read_text():
    raise SystemExit('R030 Fix1 must be materialized before Fix2')

if MARKER in builder and MARKER in base:
    print('PASS: R030 Fix2 already materialized')
    print('builder_sha256=' + sha256(BUILDER))
    print('phase0_observer_sha256=' + sha256(BASE_OBSERVER))
    raise SystemExit(0)

# ---------------------------------------------------------------------------
# 1) Migration telemetry: keep the exact Fix1 migration behavior, but emit only
# one V4_CANONICAL_ADOPTED line per body per process. Repeated EnsureEnvironment
# checks continue to re-register the canonical alias until the V5 checkpoint is
# written; only the redundant logging is suppressed.
# ---------------------------------------------------------------------------
old = '''        readonly HashSet<string> validatedEnvironments =
            new HashSet<string>(StringComparer.Ordinal);
'''
new = '''        readonly HashSet<string> validatedEnvironments =
            new HashSet<string>(StringComparer.Ordinal);
        // AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP
        // Migration authority remains unchanged; this set suppresses duplicate
        // V4_CANONICAL_ADOPTED telemetry from the five-second body refresh loop.
        readonly HashSet<string> r030Fix2MigrationLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
'''
builder = replace_once(builder, old, new, 'migration one-shot set')

old = '''                    AERISLogger.Info("[R030_FIX1][MIGRATE] body=" + body.name +
                        "; event=V4_CANONICAL_ADOPTED; environment=" +
                        plan.EnvironmentHash + "; identity=" + persistentIdentity +
                        "; witness=" + witness + "; indexed_payload=true");
'''
new = '''                    if (r030Fix2MigrationLogged.Add(body.name))
                        AERISLogger.Info("[R030_FIX1][MIGRATE] body=" + body.name +
                            "; event=V4_CANONICAL_ADOPTED; environment=" +
                            plan.EnvironmentHash + "; identity=" + persistentIdentity +
                            "; witness=" + witness +
                            "; indexed_payload=true; log_policy=ONE_SHOT_PER_BODY");
'''
builder = replace_once(builder, old, new, 'migration log one-shot')

old = '''    internal sealed class AERISTerrainPreloadBuilder : IDisposable
    {
        // AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY
'''
new = '''    internal sealed class AERISTerrainPreloadBuilder : IDisposable
    {
        // AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY
        // AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP
'''
builder = replace_once(builder, old, new, 'builder Fix2 marker')
BUILDER.write_text(builder)

# ---------------------------------------------------------------------------
# 2) Phase0 inventory observer: retain its useful PTC inventory, but understand
# V5 state layout. It must no longer report persisted_plans=0 / ready=0 after a
# successful Fix1 migration. The primitive/planet hash remains diagnostic only.
# ---------------------------------------------------------------------------
old = '''            string stateStatus;
            Dictionary<string, PersistedPlan> persisted = ReadState(selectedState, out stateStatus);
'''
new = '''            int stateVersion;
            string persistedPointSignature;
            string stateStatus;
            Dictionary<string, PersistedPlan> persisted = ReadState(selectedState,
                out stateVersion, out persistedPointSignature, out stateStatus);
'''
base = replace_once(base, old, new, 'phase0 state read call')

old = '''                "; state=" + stateStatus + "; state_path=" + Safe(selectedState) +
                "; settings_hash=" + settingsHash +
'''
new = '''                "; state=" + stateStatus + "; state_version=" + stateVersion +
                "; applied_point_signature=" + Safe(persistedPointSignature) +
                "; state_path=" + Safe(selectedState) +
                "; settings_hash=" + settingsHash +
'''
base = replace_once(base, old, new, 'phase0 session V5 metadata')

old = '''                    "; mods=" + modCount + "; structural_hash=" + structuralHash +
                    "; primitive_hash=" + primitiveHash + "; planet_hash=" + planetTerrainHash +
                    "; environment=" + currentEnvironment + "; source_hints=" + Safe(sourceHints) +
'''
new = '''                    "; mods=" + modCount + "; structural_hash=" + structuralHash +
                    "; primitive_hash=" + primitiveHash +
                    "; diagnostic_planet_hash=" + planetTerrainHash +
                    "; hash_authority=DIAGNOSTIC_ONLY" +
                    "; environment=" + currentEnvironment + "; source_hints=" + Safe(sourceHints) +
'''
base = replace_once(base, old, new, 'phase0 diagnostic hash label')

old = '''        static Dictionary<string, PersistedPlan> ReadState(string path, out string status)
        {
            var result = new Dictionary<string, PersistedPlan>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
'''
new = '''        static Dictionary<string, PersistedPlan> ReadState(string path,
            out int version, out string pointSignature, out string status)
        {
            version = 0;
            pointSignature = string.Empty;
            var result = new Dictionary<string, PersistedPlan>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
'''
base = replace_once(base, old, new, 'phase0 V4/V5 reader signature')

old = '''                    string magic = reader.ReadString();
                    int version = reader.ReadInt32();
                    if (!string.Equals(magic, AERISTerrainPreloadFormat.StateMagic,
                        StringComparison.Ordinal) || version != 4)
                    {
                        status = "UNSUPPORTED:" + version;
                        return result;
                    }
                    reader.ReadInt32(); // mode
                    int count = reader.ReadInt32();
'''
new = '''                    string magic = reader.ReadString();
                    version = reader.ReadInt32();
                    if (!string.Equals(magic, AERISTerrainPreloadFormat.StateMagic,
                        StringComparison.Ordinal) || (version != 4 && version != 5))
                    {
                        status = "UNSUPPORTED:" + version;
                        return result;
                    }
                    reader.ReadInt32(); // mode
                    if (version >= 5) pointSignature = reader.ReadString();
                    int count = reader.ReadInt32();
'''
base = replace_once(base, old, new, 'phase0 V4/V5 reader header')

old = '''                        plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        if (!string.IsNullOrEmpty(plan.BodyName)) result[plan.BodyName] = plan;
'''
new = '''                        plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        if (version >= 5)
                        {
                            reader.ReadString(); // persistent terrain identity
                            reader.ReadString(); // terrain witness
                        }
                        if (!string.IsNullOrEmpty(plan.BodyName)) result[plan.BodyName] = plan;
'''
base = replace_once(base, old, new, 'phase0 V5 body tail')

old = '''                    status = stream.Position == stream.Length ? "V4_OK" : "V4_TRAILING_DATA";
'''
new = '''                    status = stream.Position == stream.Length ?
                        "V" + version + "_OK" : "V" + version + "_TRAILING_DATA";
'''
base = replace_once(base, old, new, 'phase0 dynamic state status')

old = '''    internal sealed class AERISR030PreloadPersistencePtcPhase0Observer : MonoBehaviour
    {
        internal const string Variant = "AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER";
'''
new = '''    internal sealed class AERISR030PreloadPersistencePtcPhase0Observer : MonoBehaviour
    {
        // AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP
        internal const string Variant = "AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER";
'''
base = replace_once(base, old, new, 'phase0 Fix2 marker')
BASE_OBSERVER.write_text(base)

# Build identity only; no control/display/DB format changes.
version_text = VERSION.read_text()
version_text = version_text.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R030 FIX1 STABLE PERSISTENT TERRAIN IDENTITY',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R030 FIX2 PERSISTENCE TELEMETRY CLEANUP')
version_text = version_text.replace(
    'DEV CP3.75 — AERIS32 — REV3.5 R030 FIX1 — STABLE PERSISTENT TERRAIN IDENTITY',
    'DEV CP3.75 — AERIS32 — REV3.5 R030 FIX2 — PERSISTENCE TELEMETRY CLEANUP')
version_text = version_text.replace(
    'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY',
    'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP')
source_hash = tree_hash()
version_text = re.sub(
    r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + source_hash + '";',
    version_text)
VERSION.write_text(version_text)

final_builder = BUILDER.read_text()
final_base = BASE_OBSERVER.read_text()
checks = (
    (MARKER in final_builder, 'builder Fix2 marker'),
    (MARKER in final_base, 'phase0 Fix2 marker'),
    ('r030Fix2MigrationLogged.Add(body.name)' in final_builder,
        'migration one-shot gate'),
    ('log_policy=ONE_SHOT_PER_BODY' in final_builder,
        'migration one-shot telemetry'),
    ('version != 4 && version != 5' in final_base,
        'phase0 V4/V5 reader'),
    ('if (version >= 5) pointSignature = reader.ReadString();' in final_base,
        'phase0 V5 point signature'),
    ('diagnostic_planet_hash=' in final_base,
        'diagnostic hash label'),
    ('hash_authority=DIAGNOSTIC_ONLY' in final_base,
        'diagnostic authority label'),
)
failed = [label for ok, label in checks if not ok]
if failed:
    raise SystemExit('R030 Fix2 materialization FAIL: ' + ', '.join(failed))

print('PASS: materialized R030 Fix2 persistence telemetry cleanup')
print('runtime_change=TELEMETRY_ONLY_ON_TOP_OF_ACCEPTED_FIX1')
print('migration_log=ONE_SHOT_PER_BODY_PER_PROCESS')
print('phase0_state_reader=V4_V5')
print('phase0_runtime_hash=DIAGNOSTIC_ONLY')
print('builder_sha256=' + sha256(BUILDER))
print('phase0_observer_sha256=' + sha256(BASE_OBSERVER))
print('source_tree_sha256=' + source_hash)
