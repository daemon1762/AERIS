#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OH = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
CACHE = ROOT / 'Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs'
RENDERER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
BUILD = ROOT / 'build_ubuntu.sh'
MARKER = 'AERIS26_REV003_OBSERVER_M1'


def fail(msg):
    raise SystemExit('[AERIS26 REV003 OBSERVER M1] ' + msg)


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        fail('%s anchor count=%d' % (label, count))
    return text.replace(old, new, 1)


def require(text, needle, label):
    if needle not in text:
        fail('missing %s: %s' % (label, needle))


for path in (OH, CACHE, RENDERER, BUILD):
    if not path.is_file():
        fail('missing source file: ' + str(path))

renderer = RENDERER.read_text()
require(renderer, 'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION', 'REV003 renderer marker')
for forbidden in (
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    if forbidden in renderer:
        fail('refusing non-REV003 runtime tree; found ' + forbidden)

oh = OH.read_text()
if MARKER in oh:
    print('[AERIS26 REV003 OBSERVER M1] patch already present')
    sys.exit(0)
require(oh, 'internal const string Codename = "NOREPINEPHRINE";', 'REV003 codename')
require(oh, 'internal const string Revision = "OH_PHASE6_003";', 'REV003 revision')
require(oh, 'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";', 'REV003 candidate')

if 'using System.Collections.Generic;' not in oh:
    oh = replace_once(oh, 'using System;\n', 'using System;\nusing System.Collections.Generic;\n', 'OH using')

oh = replace_once(
    oh,
    '        internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";\n',
    '        internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";\n'
    '        internal const string ObserverVariant = "AERIS26_REV003_OBSERVER_M1";\n',
    'observer identity')

observer_fields = r'''
        const int Rev003ObserverMapLimit = 16384;
        static readonly object rev003ObserverSync = new object();
        static readonly Dictionary<string, long> rev003ObserverLastHitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverLastEvictionTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverDecodeSubmitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverResidentCommitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly long[] rev003ObserverReuseBuckets = new long[6];
        static readonly long[] rev003ObserverRerequestBuckets = new long[6];
        static readonly long[] rev003ObserverDecodeBuckets = new long[6];
        static readonly long[] rev003ObserverResidentLifeBuckets = new long[6];
        static readonly long[] rev003ObserverEvictionIdleBuckets = new long[6];
        static readonly long[] rev003ObserverLodEvents = new long[5];
        static long rev003ObserverAccessTotal;
        static long rev003ObserverPrepHit;
        static long rev003ObserverDecodeSubmit;
        static long rev003ObserverGetHit;
        static long rev003ObserverGetMiss;
        static long rev003ObserverRamCommit;
        static long rev003ObserverDecodeFailure;
        static long rev003ObserverEvictions;
        static long rev003ObserverBudgetEvictions;
        static long rev003ObserverEvictedBytes;
        static long rev003ObserverReuseSamples;
        static double rev003ObserverReuseTotalMs;
        static double rev003ObserverReuseMaxMs;
        static long rev003ObserverRerequestSamples;
        static double rev003ObserverRerequestTotalMs;
        static double rev003ObserverRerequestMaxMs;
        static long rev003ObserverDecodeSamples;
        static double rev003ObserverDecodeTotalMs;
        static double rev003ObserverDecodeMaxMs;
        static long rev003ObserverResidentLifeSamples;
        static double rev003ObserverResidentLifeTotalMs;
        static double rev003ObserverResidentLifeMaxMs;
        static long rev003ObserverEvictionIdleSamples;
        static double rev003ObserverEvictionIdleTotalMs;
        static double rev003ObserverEvictionIdleMaxMs;
        static long rev003ObserverScopeResets;
        static long rev003ObserverMapResets;
        static long rev003ObserverSelfCalls;
        static long rev003ObserverSelfTicks;
        static long rev003ObserverSelfMaxTicks;
'''
oh = replace_once(
    oh,
    '        static AERISOperationHealthPenicillin current;\n',
    '        static AERISOperationHealthPenicillin current;\n' + observer_fields,
    'observer fields')

observer_methods = r'''
        internal static void RecordRev003ObserverAccess(string stableId, int lod,
            int kind, long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverAccessTotal++;
                if (kind == 1) rev003ObserverPrepHit++;
                else if (kind == 2) rev003ObserverDecodeSubmit++;
                else if (kind == 3) rev003ObserverGetHit++;
                else if (kind == 4) rev003ObserverGetMiss++;

                if (lod >= 0 && lod < rev003ObserverLodEvents.Length)
                    rev003ObserverLodEvents[lod]++;

                bool hit = kind == 1 || kind == 3;
                if (hit)
                {
                    long previousHit;
                    if (rev003ObserverLastHitTicks.TryGetValue(stableId,
                        out previousHit) && previousHit > 0L && now >= previousHit)
                    {
                        double ageMs = (now - previousHit) * 1000.0 /
                            Stopwatch.Frequency;
                        if (FiniteNonNegative(ageMs))
                        {
                            rev003ObserverReuseSamples++;
                            rev003ObserverReuseTotalMs += ageMs;
                            rev003ObserverReuseMaxMs = Math.Max(
                                rev003ObserverReuseMaxMs, ageMs);
                            rev003ObserverReuseBuckets[ObserverAgeBucket(ageMs)]++;
                        }
                    }
                    rev003ObserverLastHitTicks[stableId] = now;
                }

                long evicted;
                if (rev003ObserverLastEvictionTicks.TryGetValue(stableId,
                    out evicted) && evicted > 0L && now >= evicted)
                {
                    double ageMs = (now - evicted) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(ageMs))
                    {
                        rev003ObserverRerequestSamples++;
                        rev003ObserverRerequestTotalMs += ageMs;
                        rev003ObserverRerequestMaxMs = Math.Max(
                            rev003ObserverRerequestMaxMs, ageMs);
                        rev003ObserverRerequestBuckets[ObserverAgeBucket(ageMs)]++;
                    }
                    rev003ObserverLastEvictionTicks.Remove(stableId);
                }

                if (kind == 2 &&
                    !rev003ObserverDecodeSubmitTicks.ContainsKey(stableId))
                    rev003ObserverDecodeSubmitTicks[stableId] = now;

                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverRamCommit(string stableId, int lod,
            long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverRamCommit++;
                long submitted;
                if (rev003ObserverDecodeSubmitTicks.TryGetValue(stableId,
                    out submitted) && submitted > 0L && now >= submitted)
                {
                    double latencyMs = (now - submitted) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(latencyMs))
                    {
                        rev003ObserverDecodeSamples++;
                        rev003ObserverDecodeTotalMs += latencyMs;
                        rev003ObserverDecodeMaxMs = Math.Max(
                            rev003ObserverDecodeMaxMs, latencyMs);
                        rev003ObserverDecodeBuckets[ObserverDecodeBucket(latencyMs)]++;
                    }
                    rev003ObserverDecodeSubmitTicks.Remove(stableId);
                }
                rev003ObserverResidentCommitTicks[stableId] = now;
                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverDecodeFailure(string stableId)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;
            long start = Stopwatch.GetTimestamp();
            lock (rev003ObserverSync)
            {
                rev003ObserverDecodeFailure++;
                rev003ObserverDecodeSubmitTicks.Remove(stableId);
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverEviction(string stableId, int lod,
            bool budget, long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverEvictions++;
                if (budget) rev003ObserverBudgetEvictions++;
                rev003ObserverEvictedBytes += Math.Max(0L, bytes);
                rev003ObserverLastEvictionTicks[stableId] = now;

                long committed;
                if (rev003ObserverResidentCommitTicks.TryGetValue(stableId,
                    out committed) && committed > 0L && now >= committed)
                {
                    double lifeMs = (now - committed) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(lifeMs))
                    {
                        rev003ObserverResidentLifeSamples++;
                        rev003ObserverResidentLifeTotalMs += lifeMs;
                        rev003ObserverResidentLifeMaxMs = Math.Max(
                            rev003ObserverResidentLifeMaxMs, lifeMs);
                        rev003ObserverResidentLifeBuckets[ObserverAgeBucket(lifeMs)]++;
                    }
                    rev003ObserverResidentCommitTicks.Remove(stableId);
                }

                long hit;
                if (rev003ObserverLastHitTicks.TryGetValue(stableId, out hit) &&
                    hit > 0L && now >= hit)
                {
                    double idleMs = (now - hit) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(idleMs))
                    {
                        rev003ObserverEvictionIdleSamples++;
                        rev003ObserverEvictionIdleTotalMs += idleMs;
                        rev003ObserverEvictionIdleMaxMs = Math.Max(
                            rev003ObserverEvictionIdleMaxMs, idleMs);
                        rev003ObserverEvictionIdleBuckets[ObserverAgeBucket(idleMs)]++;
                    }
                }

                rev003ObserverDecodeSubmitTicks.Remove(stableId);
                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverScopeReset()
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off) return;
            long start = Stopwatch.GetTimestamp();
            lock (rev003ObserverSync)
            {
                rev003ObserverScopeResets++;
                rev003ObserverLastHitTicks.Clear();
                rev003ObserverLastEvictionTicks.Clear();
                rev003ObserverDecodeSubmitTicks.Clear();
                rev003ObserverResidentCommitTicks.Clear();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        static int ObserverAgeBucket(double milliseconds)
        {
            if (milliseconds < 1000.0) return 0;
            if (milliseconds < 5000.0) return 1;
            if (milliseconds < 15000.0) return 2;
            if (milliseconds < 60000.0) return 3;
            if (milliseconds < 300000.0) return 4;
            return 5;
        }

        static int ObserverDecodeBucket(double milliseconds)
        {
            if (milliseconds < 5.0) return 0;
            if (milliseconds < 20.0) return 1;
            if (milliseconds < 50.0) return 2;
            if (milliseconds < 100.0) return 3;
            if (milliseconds < 250.0) return 4;
            return 5;
        }

        static void BoundRev003ObserverMapsLocked()
        {
            if (rev003ObserverLastHitTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverLastEvictionTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverDecodeSubmitTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverResidentCommitTicks.Count <= Rev003ObserverMapLimit)
                return;

            rev003ObserverLastHitTicks.Clear();
            rev003ObserverLastEvictionTicks.Clear();
            rev003ObserverDecodeSubmitTicks.Clear();
            rev003ObserverResidentCommitTicks.Clear();
            rev003ObserverMapResets++;
        }

        static void RecordRev003ObserverSelfLocked(long startTicks)
        {
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - startTicks);
            rev003ObserverSelfCalls++;
            rev003ObserverSelfTicks += elapsed;
            rev003ObserverSelfMaxTicks = Math.Max(
                rev003ObserverSelfMaxTicks, elapsed);
        }

        static string ObserverHistogram(long[] buckets)
        {
            return buckets[0] + "/" + buckets[1] + "/" + buckets[2] + "/" +
                buckets[3] + "/" + buckets[4] + "/" + buckets[5];
        }

        static string ObserverLodHistogram(long[] buckets)
        {
            return buckets[0] + "/" + buckets[1] + "/" + buckets[2] + "/" +
                buckets[3] + "/" + buckets[4];
        }

        static string Rev003ObserverSummary()
        {
            lock (rev003ObserverSync)
            {
                double reuseMean = rev003ObserverReuseSamples > 0L ?
                    rev003ObserverReuseTotalMs / rev003ObserverReuseSamples : 0.0;
                double rerequestMean = rev003ObserverRerequestSamples > 0L ?
                    rev003ObserverRerequestTotalMs / rev003ObserverRerequestSamples : 0.0;
                double decodeMean = rev003ObserverDecodeSamples > 0L ?
                    rev003ObserverDecodeTotalMs / rev003ObserverDecodeSamples : 0.0;
                double lifeMean = rev003ObserverResidentLifeSamples > 0L ?
                    rev003ObserverResidentLifeTotalMs / rev003ObserverResidentLifeSamples : 0.0;
                double idleMean = rev003ObserverEvictionIdleSamples > 0L ?
                    rev003ObserverEvictionIdleTotalMs / rev003ObserverEvictionIdleSamples : 0.0;
                double selfMeanUs = rev003ObserverSelfCalls > 0L ?
                    (rev003ObserverSelfTicks * 1000000.0 / Stopwatch.Frequency) /
                        rev003ObserverSelfCalls : 0.0;
                double selfMaxUs = rev003ObserverSelfMaxTicks * 1000000.0 /
                    Stopwatch.Frequency;

                return "; obs_variant=" + ObserverVariant +
                    "; obs_access=" + rev003ObserverAccessTotal +
                    "; obs_prep_hit=" + rev003ObserverPrepHit +
                    "; obs_decode_submit=" + rev003ObserverDecodeSubmit +
                    "; obs_get_hit=" + rev003ObserverGetHit +
                    "; obs_get_miss=" + rev003ObserverGetMiss +
                    "; obs_ram_commit=" + rev003ObserverRamCommit +
                    "; obs_decode_fail=" + rev003ObserverDecodeFailure +
                    "; obs_evict=" + rev003ObserverEvictions +
                    "; obs_budget_evict=" + rev003ObserverBudgetEvictions +
                    "; obs_evict_mib=" + F3(rev003ObserverEvictedBytes / 1048576.0) +
                    "; obs_reuse_samples=" + rev003ObserverReuseSamples +
                    "; obs_reuse_mean_ms=" + F3(reuseMean) +
                    "; obs_reuse_max_ms=" + F3(rev003ObserverReuseMaxMs) +
                    "; obs_reuse_hist=" + ObserverHistogram(rev003ObserverReuseBuckets) +
                    "; obs_rereq_samples=" + rev003ObserverRerequestSamples +
                    "; obs_rereq_mean_ms=" + F3(rerequestMean) +
                    "; obs_rereq_max_ms=" + F3(rev003ObserverRerequestMaxMs) +
                    "; obs_rereq_hist=" + ObserverHistogram(rev003ObserverRerequestBuckets) +
                    "; obs_decode_samples=" + rev003ObserverDecodeSamples +
                    "; obs_decode_mean_ms=" + F3(decodeMean) +
                    "; obs_decode_max_ms=" + F3(rev003ObserverDecodeMaxMs) +
                    "; obs_decode_hist=" + ObserverHistogram(rev003ObserverDecodeBuckets) +
                    "; obs_reslife_samples=" + rev003ObserverResidentLifeSamples +
                    "; obs_reslife_mean_s=" + F3(lifeMean / 1000.0) +
                    "; obs_reslife_max_s=" + F3(rev003ObserverResidentLifeMaxMs / 1000.0) +
                    "; obs_reslife_hist=" + ObserverHistogram(rev003ObserverResidentLifeBuckets) +
                    "; obs_evict_idle_samples=" + rev003ObserverEvictionIdleSamples +
                    "; obs_evict_idle_mean_s=" + F3(idleMean / 1000.0) +
                    "; obs_evict_idle_max_s=" + F3(rev003ObserverEvictionIdleMaxMs / 1000.0) +
                    "; obs_evict_idle_hist=" + ObserverHistogram(rev003ObserverEvictionIdleBuckets) +
                    "; obs_lod_evt_g_f_r_l_land=" + ObserverLodHistogram(rev003ObserverLodEvents) +
                    "; obs_maps=" + rev003ObserverLastHitTicks.Count + "/" +
                        rev003ObserverLastEvictionTicks.Count + "/" +
                        rev003ObserverDecodeSubmitTicks.Count + "/" +
                        rev003ObserverResidentCommitTicks.Count +
                    "; obs_scope_reset=" + rev003ObserverScopeResets +
                    "; obs_map_reset=" + rev003ObserverMapResets +
                    "; obs_self_mean_us=" + F3(selfMeanUs) +
                    "; obs_self_max_us=" + F3(selfMaxUs);
            }
        }
'''
oh = replace_once(
    oh,
    '        internal static bool Enabled\n',
    observer_methods + '\n        internal static bool Enabled\n',
    'observer methods')

oh = replace_once(
    oh,
    '                    "; candidate=" + Candidate +\n                    "; level=" + logLevel.ToString().ToUpperInvariant() +\n',
    '                    "; candidate=" + Candidate +\n                    "; observer_variant=" + ObserverVariant +\n                    "; measurement_only=1" +\n                    "; control_delta=0" +\n                    "; level=" + logLevel.ToString().ToUpperInvariant() +\n',
    'observer startup identity')

oh = replace_once(
    oh,
    '                "; oh_self_ema_ms=" + F3(value.SelfEmaMs) +\n                "; log_deferred=" + value.DeferredSummaries +\n                "; attribution=PARTIAL_PASSIVE");',
    '                "; oh_self_ema_ms=" + F3(value.SelfEmaMs) +\n                "; log_deferred=" + value.DeferredSummaries +\n                Rev003ObserverSummary() +\n                "; attribution=PARTIAL_PASSIVE");',
    'observer summary append')

cache = CACHE.read_text()
if 'AERISOperationHealthPenicillin.RecordRev003ObserverAccess' in cache:
    fail('cache observer hooks already present while OH marker absent')
if 'using AERISFlightControl.Performance;' not in cache:
    cache = replace_once(
        cache,
        'using AERISFlightControl.Logging;\n',
        'using AERISFlightControl.Logging;\nusing AERISFlightControl.Performance;\n',
        'cache performance using')

cache = replace_once(
    cache,
    '                AERISLogger.Info("[CP3_RESIDENT] scope=" + scopeGeneration +\n                    " bodyGen=" + bodyGeneration + " dbGen=" + databaseGeneration +\n                    " body=" + (string.IsNullOrEmpty(activeBody) ? "<none>" : activeBody) +\n                    " active=" + (active ? "1" : "0") +\n                    " reason=" + (lastCause.Length == 0 ? reason.ToString() : lastCause) +\n                    " payloadRoute=ASYNC_DECODE_RAM_RESIDENT");\n                return true;',
    '                AERISLogger.Info("[CP3_RESIDENT] scope=" + scopeGeneration +\n                    " bodyGen=" + bodyGeneration + " dbGen=" + databaseGeneration +\n                    " body=" + (string.IsNullOrEmpty(activeBody) ? "<none>" : activeBody) +\n                    " active=" + (active ? "1" : "0") +\n                    " reason=" + (lastCause.Length == 0 ? reason.ToString() : lastCause) +\n                    " payloadRoute=ASYNC_DECODE_RAM_RESIDENT");\n                AERISOperationHealthPenicillin.RecordRev003ObserverScopeReset();\n                return true;',
    'body scope hook')

cache = replace_once(
    cache,
    '                lastCause = cause ?? string.Empty;\n                status = "INACTIVE — SCENE RESET";\n',
    '                lastCause = cause ?? string.Empty;\n                status = "INACTIVE — SCENE RESET";\n                AERISOperationHealthPenicillin.RecordRev003ObserverScopeReset();\n',
    'scene reset hook')

cache = replace_once(
    cache,
    '                    TouchLocked(entry);\n                    UpdateStatusLocked();\n                    return true;\n                }\n\n                if (entry.State == AERISResidentTileState.Indexed)',
    '                    TouchLocked(entry);\n                    UpdateStatusLocked();\n                    AERISOperationHealthPenicillin.RecordRev003ObserverAccess(\n                        key.StableId, (int)key.Lod, 1, entry.RamBytes);\n                    return true;\n                }\n\n                if (entry.State == AERISResidentTileState.Indexed)',
    'prepare hit hook')

cache = replace_once(
    cache,
    '                asyncDecodeSubmissions++;\n                token = TokenForLocked(entry);\n                TouchLocked(entry);\n                UpdateStatusLocked();\n                return true;',
    '                asyncDecodeSubmissions++;\n                token = TokenForLocked(entry);\n                TouchLocked(entry);\n                UpdateStatusLocked();\n                AERISOperationHealthPenicillin.RecordRev003ObserverAccess(\n                    key.StableId, (int)key.Lod, 2, entry.RamBytes);\n                return true;',
    'decode submit hook')

cache = replace_once(
    cache,
    '                asyncDecodeFailures++;\n                lastCause = cause ?? string.Empty;\n                TouchLocked(entry);\n                UpdateStatusLocked();\n',
    '                asyncDecodeFailures++;\n                lastCause = cause ?? string.Empty;\n                TouchLocked(entry);\n                UpdateStatusLocked();\n                AERISOperationHealthPenicillin.RecordRev003ObserverDecodeFailure(\n                    token.StableId);\n',
    'decode failure hook')

cache = replace_once(
    cache,
    '                result = AERISResidentCommitResult.Committed;\n                UpdateStatusLocked();\n                return true;',
    '                result = AERISResidentCommitResult.Committed;\n                UpdateStatusLocked();\n                AERISOperationHealthPenicillin.RecordRev003ObserverRamCommit(\n                    entry.Key.StableId, (int)entry.Key.Lod, incomingBytes);\n                return true;',
    'ram commit hook')

cache = replace_once(
    cache,
    '                {\n                    cacheMisses++;\n                    return false;\n                }\n                cacheHits++;\n                tile = entry.ResidentTile;',
    '                {\n                    cacheMisses++;\n                    AERISOperationHealthPenicillin.RecordRev003ObserverAccess(\n                        key.StableId, (int)key.Lod, 4, 0L);\n                    return false;\n                }\n                cacheHits++;\n                AERISOperationHealthPenicillin.RecordRev003ObserverAccess(\n                    key.StableId, (int)key.Lod, 3, entry.RamBytes);\n                tile = entry.ResidentTile;',
    'get hit/miss hook')

cache = replace_once(
    cache,
    '            ramBytes = Math.Max(0L, ramBytes - Math.Max(0L, entry.RamBytes));\n            pinLeaseCount = Math.Max(0, pinLeaseCount - Math.Max(0, entry.PinCount));',
    '            AERISOperationHealthPenicillin.RecordRev003ObserverEviction(\n                entry.Key.StableId, (int)entry.Key.Lod,\n                reason == AERISResidentEvictionReason.Budget, entry.RamBytes);\n            ramBytes = Math.Max(0L, ramBytes - Math.Max(0L, entry.RamBytes));\n            pinLeaseCount = Math.Max(0, pinLeaseCount - Math.Max(0, entry.PinCount));',
    'eviction hook')

build = BUILD.read_text()
if 'OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"' not in build:
    build = replace_once(
        build,
        'CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"\n',
        'CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"\n'
        'OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"\n',
        'build observer identity')

    build = replace_once(
        build,
        '      Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs \\\n',
        '      Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs \\\n'
        '      Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs \\\n'
        '      Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs \\\n',
        'source hash coverage')

    build = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris26_rev003_observer.py"\n',
        'build observer verifier')

    build = replace_once(
        build,
        '  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        '  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'observer_variant=%s\\n\' "$OBSERVER_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate observer identity')

OH.write_text(oh)
CACHE.write_text(cache)
BUILD.write_text(build)
print('[AERIS26 REV003 OBSERVER M1] APPLY PASS')
print('behavior_base=NOREPINEPHRINE_OH_PHASE6_003')
print('observer_variant=' + MARKER)
print('control_delta=0')
print('measurement_io=existing_OH_summary_only')
