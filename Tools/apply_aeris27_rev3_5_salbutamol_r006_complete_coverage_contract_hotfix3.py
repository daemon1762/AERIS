#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
D = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 COMPLETE COVERAGE CONTRACT HOTFIX3]'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0:
        fail('method open missing: ' + signature)
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return start, i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    fail('method close missing: ' + signature)


if not T.is_file() or not D.is_file() or not B.is_file():
    fail('required generated files missing')
tile = T.read_text()
db = D.read_text()
build = B.read_text()
if HF3 in tile:
    print(PREFIX + ' already present')
    raise SystemExit(0)
if 'REV3_5_R006_HOTFIX2="' + HF2 + '"' not in build:
    fail('R006 HF2 generated parent required')

# Hotfix3 source identity and telemetry live in the tile system because the failure is a
# source-authority regression: a complete 7x7 Preview was being replaced by a 25/50/75%
# 33x33 Final progressive commit under the same StableId. Keep complete coverage monotonic.
tile, _ = replace_once(
    tile,
    '        readonly AERISTerrainTileCacheTelemetry telemetry = new AERISTerrainTileCacheTelemetry();\n\n'
    '        CelestialBody activeBody;\n',
    '        readonly AERISTerrainTileCacheTelemetry telemetry = new AERISTerrainTileCacheTelemetry();\n'
    '        // ' + HF3 + ': complete visible terrain authority is monotonic. A complete\n'
    '        // Preview may be replaced by a complete higher-fidelity Final, never by an\n'
    '        // in-progress Final commit. This repairs the cold-foundation BLACK deadlock.\n'
    '        const string Rev35R006CompleteCoverageHotfix3 = "' + HF3 + '";\n'
    '        long operationHealthRev35R006Hf3PreservedCompleteAuthority;\n'
    '        long operationHealthRev35R006Hf3IncompleteStageRetries;\n'
    '        int operationHealthRev35R006Hf3WorstIncompleteQuality = 100;\n\n'
    '        CelestialBody activeBody;\n',
    'HF3 identity/telemetry')

helper = r'''        static bool IsCompleteCoverageAuthority(AERISTerrainHeightTile tile)
        {
            if (tile == null || !tile.SamplingComplete || tile.Quality < 100 ||
                tile.Resolution < 2 || tile.Elevation == null || tile.Flags == null)
                return false;
            long expected = (long)tile.Resolution * tile.Resolution;
            return tile.Elevation.LongLength == expected &&
                tile.Flags.LongLength == expected;
        }

        static bool CanReplaceCompleteCoverageAuthority(AERISTerrainHeightTile existing,
            AERISTerrainHeightTile incoming)
        {
            if (!IsCompleteCoverageAuthority(existing)) return true;
            if (!IsCompleteCoverageAuthority(incoming)) return false;
            if (incoming.Resolution > existing.Resolution) return true;
            if (incoming.Resolution < existing.Resolution) return false;
            if (existing.IsPreview && !incoming.IsPreview) return true;
            if (!existing.IsPreview && incoming.IsPreview) return false;
            return true;
        }

'''
anchor = '        static bool ReconcileRequestWithRamTile(AERISTerrainTileRequest request,\n'
if helper.strip() not in tile:
    if tile.count(anchor) != 1:
        fail('Reconcile helper anchor mismatch')
    tile = tile.replace(anchor, helper + anchor, 1)

# SamplingComplete means every block finished; it does not prove every PQS sample was
# valid. Only Quality=100 can satisfy the complete-coverage source authority contract.
old_reconcile = '''            bool samplingComplete = tile.SamplingComplete;
            if (samplingComplete && !tile.IsPreview &&
                tile.Resolution >= request.FinalResolution) return true;
            if (samplingComplete &&
                (tile.IsPreview || tile.Resolution < request.FinalResolution))
'''
new_reconcile = '''            bool samplingComplete = tile.SamplingComplete;
            bool completeCoverage = IsCompleteCoverageAuthority(tile);
            if (completeCoverage && !tile.IsPreview &&
                tile.Resolution >= request.FinalResolution) return true;
            if (completeCoverage &&
                (tile.IsPreview || tile.Resolution < request.FinalResolution))
'''
tile, _ = replace_once(tile, old_reconcile, new_reconcile,
                       'RAM complete-coverage reconcile')

# Replace only CommitFlightBlock. The worker keeps refining in the background, but a
# progressive Final result may no longer evict a complete Preview from the RAM authority.
start, end = method_bounds(tile,
    '        void CommitFlightBlock(AERISTerrainTileRequest request,')
new_commit = r'''        void CommitFlightBlock(AERISTerrainTileRequest request,
            AERISTerrainHeightTile tile, bool requestComplete)
        {
            if (request == null || tile == null) return;
            if (!IsFlightRequestCurrent(request))
            {
                telemetry.StaleCancelled++;
                preloadTelemetry.StaleResultsDiscarded++;
                return;
            }
            tile.Source = AERISTerrainTileSource.RealtimeGenerated;
            tile.PqsConfigurationHash = environmentHash;
            tile.GameDataHash = GameDataHash;
            tile.TerrainGenerationId = preloadDatabase.DatabaseGeneration;

            AERISTerrainHeightTile existing;
            bool hasExisting = ram.TryGet(request.Key, out existing) && existing != null;
            bool preserveCompleteAuthority = hasExisting &&
                IsCompleteCoverageAuthority(existing) &&
                !CanReplaceCompleteCoverageAuthority(existing, tile);
            if (preserveCompleteAuthority)
            {
                operationHealthRev35R006Hf3PreservedCompleteAuthority++;
                operationHealthRev35R006Hf3WorstIncompleteQuality = Math.Min(
                    operationHealthRev35R006Hf3WorstIncompleteQuality,
                    Math.Max(0, Math.Min(100, tile.Quality)));
                if (requestComplete)
                {
                    operationHealthRev35R006Hf3IncompleteStageRetries++;
                    // The just-completed pipeline state is removed after this callback.
                    // Do not synchronously re-enqueue the same WorkId here. Force the next
                    // planner pass to request the stage again while the complete Preview
                    // remains visible and foundation-complete.
                    nextPlanRealtime = 0f;
                    status = "TERRAIN COMPLETE AUTHORITY PRESERVED / RETRY QUEUED";
                }
                else
                    status = "TERRAIN COMPLETE AUTHORITY PRESERVED / FINAL REFINEMENT";
                return;
            }

            ram.Put(tile);
            telemetry.Generated++;
            terrainGeneration++;
            lastTerrainResultRealtime = Time.realtimeSinceStartup;
            if (!requestComplete)
            {
                status = "TERRAIN BLOCK PROGRESSIVE COMMIT " + tile.Quality + "%";
                return;
            }
            if (request.Stage == AERISTerrainSamplingStage.Preview && tile.IsPreview)
            {
                if (!IsCompleteCoverageAuthority(tile))
                {
                    operationHealthRev35R006Hf3IncompleteStageRetries++;
                    operationHealthRev35R006Hf3WorstIncompleteQuality = Math.Min(
                        operationHealthRev35R006Hf3WorstIncompleteQuality,
                        Math.Max(0, Math.Min(100, tile.Quality)));
                    nextPlanRealtime = 0f;
                    status = "TERRAIN PREVIEW COVERAGE INCOMPLETE / RETRY";
                    return;
                }
                telemetry.PreviewGenerated++;
                status = "TERRAIN PREVIEW AVAILABLE";
                if (desiredRequestIds.Contains(request.Key.StableId))
                    EnsureRequest(CloneForFinal(request));
                return;
            }
            tile.IsPreview = false;
            if (!IsCompleteCoverageAuthority(tile))
            {
                operationHealthRev35R006Hf3IncompleteStageRetries++;
                operationHealthRev35R006Hf3WorstIncompleteQuality = Math.Min(
                    operationHealthRev35R006Hf3WorstIncompleteQuality,
                    Math.Max(0, Math.Min(100, tile.Quality)));
                nextPlanRealtime = 0f;
                status = "TERRAIN FINAL COVERAGE INCOMPLETE / RETRY";
                return;
            }
            telemetry.FinalGenerated++;
            status = tile.Key.Lod >= AERISTerrainTileLod.Local ?
                "LOCAL TERRAIN AVAILABLE" : "GLOBAL TERRAIN AVAILABLE";
            ScheduleDiskWrite(tile.CloneImmutable());
        }'''
tile = tile[:start] + new_commit + tile[end:]

# Add compact runtime witnesses to the existing CP3 telemetry line.
tile, _ = replace_once(
    tile,
    '                        "; foundation_missing=" + lastFoundationMissingCount + "/" +\n'
    '                        lastFoundationRequestedCount + ".");\n',
    '                        "; foundation_missing=" + lastFoundationMissingCount + "/" +\n'
    '                        lastFoundationRequestedCount +\n'
    '                        "; hf3_preserve=" +\n'
    '                        operationHealthRev35R006Hf3PreservedCompleteAuthority +\n'
    '                        "; hf3_retry=" +\n'
    '                        operationHealthRev35R006Hf3IncompleteStageRetries +\n'
    '                        "; hf3_worst_q=" +\n'
    '                        operationHealthRev35R006Hf3WorstIncompleteQuality + ".");\n',
    'HF3 CP3 telemetry')

# The preload database must never advertise or persist a structurally finished but
# incomplete-coverage tile as a complete reusable authority. Quarantine bad metadata in
# DRAM publication instead of destructively deleting the body/database at Flight time.
db, _ = replace_once(
    db,
    '                return tileIndex.TryGetValue(key.StableId, out entry) && entry != null &&\n'
    '                    entry.State == AERISTerrainGenerationState.Complete;\n',
    '                return tileIndex.TryGetValue(key.StableId, out entry) && entry != null &&\n'
    '                    entry.State == AERISTerrainGenerationState.Complete &&\n'
    '                    entry.Quality >= 100;\n',
    'fallback Contains quality guard')

db, _ = replace_once(
    db,
    '                if (!tileIndex.TryGetValue(key.StableId, out entry) || entry == null ||\n'
    '                    entry.State != AERISTerrainGenerationState.Complete)\n',
    '                if (!tileIndex.TryGetValue(key.StableId, out entry) || entry == null ||\n'
    '                    entry.State != AERISTerrainGenerationState.Complete ||\n'
    '                    entry.Quality < 100)\n',
    'fallback chunk-id quality guard')

db, _ = replace_once(
    db,
    '                    if (entry == null ||\n'
    '                        entry.State != AERISTerrainGenerationState.Complete ||\n'
    '                        !string.Equals(entry.Key.BodyName, normalizedBody,\n',
    '                    if (entry == null ||\n'
    '                        entry.State != AERISTerrainGenerationState.Complete ||\n'
    '                        entry.Quality < 100 ||\n'
    '                        !string.Equals(entry.Key.BodyName, normalizedBody,\n',
    'resident snapshot quality guard')

db, _ = replace_once(
    db,
    '                if (tile == null || tile.IsPreview || !tile.SamplingComplete ||\n'
    '                    tile.Elevation == null || tile.Flags == null) continue;\n',
    '                if (tile == null || tile.IsPreview || !tile.SamplingComplete ||\n'
    '                    tile.Quality < 100 || tile.Elevation == null ||\n'
    '                    tile.Flags == null) continue;\n',
    'preload persistence complete-coverage guard')

db, _ = replace_once(
    db,
    '            foreach (IndexEntry entry in tileIndex.Values)\n'
    '            {\n'
    '                if (entry == null || string.IsNullOrEmpty(entry.StableId)) continue;\n'
    '                AERISMapTerrainIndexEntry published;\n',
    '            foreach (IndexEntry entry in tileIndex.Values)\n'
    '            {\n'
    '                if (entry == null || string.IsNullOrEmpty(entry.StableId) ||\n'
    '                    entry.State != AERISTerrainGenerationState.Complete ||\n'
    '                    entry.Quality < 100) continue;\n'
    '                AERISMapTerrainIndexEntry published;\n',
    'Map DRAM complete-coverage quarantine')

# Build/runtime identity. HF3 remains a source-authority repair layered after R006 HF2.
if 'REV3_5_R006_HOTFIX3="' + HF3 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_HOTFIX2="' + HF2 + '"\n',
        'REV3_5_R006_HOTFIX2="' + HF2 + '"\n'
        'REV3_5_R006_HOTFIX3="' + HF3 + '"\n',
        'build HF3 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"\n',
        'build HF3 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_hotfix2=%s\\n\' "$REV3_5_R006_HOTFIX2" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_hotfix2=%s\\n\' "$REV3_5_R006_HOTFIX2" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r006_hotfix3=%s\\n\' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate HF3 identity')

T.write_text(tile)
D.write_text(db)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('hotfix=' + HF3)
print('authority=COMPLETE_PREVIEW -> COMPLETE_FINAL monotonic; progressive Final cannot regress complete RAM authority')
print('persistence=Quality100 required; bad metadata quarantined from Map DRAM without body/database wipe')
print('quality_threshold=100 exact; 10Hz_change=0 160km_change=0 worker_count_change=0 publication_change=0')
