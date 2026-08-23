#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
TILE = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

MARKER = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
BASE_MARKER = 'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER'


def replace_once(text, old, new, label):
    if old not in text:
        raise SystemExit('R030 Fix1 anchor missing: ' + label)
    if text.count(old) != 1:
        raise SystemExit('R030 Fix1 anchor not unique: ' + label + ' count=' + str(text.count(old)))
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


for required in (BUILDER, TILE, CSPROJ, VERSION):
    if not required.is_file():
        raise SystemExit('R030 Fix1 required file missing: ' + str(required))

builder = BUILDER.read_text()
tile = TILE.read_text()

# Idempotent rerun after a successful materialization.
if MARKER in builder and MARKER in tile and OBSERVER.is_file():
    print('PASS: R030 Fix1 already materialized')
    print('builder_sha256=' + sha256(BUILDER))
    print('tile_sha256=' + sha256(TILE))
    print('observer_sha256=' + sha256(OBSERVER))
    raise SystemExit(0)

base_observer = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
if not base_observer.is_file() or BASE_MARKER not in base_observer.read_text():
    raise SystemExit('R030 Phase0 observer must be materialized before Fix1')

# ---------------------------------------------------------------------------
# Tile-system: validated canonical environment alias + stable structural/witness IDs.
# Existing EnvironmentHash generation remains available as fail-closed fallback.
# ---------------------------------------------------------------------------
old = '''        static readonly Dictionary<string, string> cachedBodyEnvironmentHashes =
            new Dictionary<string, string>(StringComparer.Ordinal);
'''
new = '''        static readonly Dictionary<string, string> cachedBodyEnvironmentHashes =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY
        // A validated persisted canonical environment ID keeps an already-built DB
        // addressable across process restarts. The old runtime-primitive fingerprint
        // remains a fail-closed fallback whenever stable identity/witness validation
        // is unavailable or changes.
        internal const string R030Fix1Variant =
            "AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY";
        static readonly Dictionary<string, string> r030Fix1CanonicalEnvironment =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> r030Fix1PersistentIdentityCache =
            new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> r030Fix1WitnessCache =
            new Dictionary<string, string>(StringComparer.Ordinal);
'''
tile = replace_once(tile, old, new, 'tile environment fields')

anchor = '''        internal static string EnvironmentHashForBody(CelestialBody body)
        {
'''
insert = r'''        internal static void SetR030Fix1CanonicalEnvironment(string bodyName,
            string environmentHash)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (environmentSync)
            {
                if (string.IsNullOrEmpty(environmentHash))
                    r030Fix1CanonicalEnvironment.Remove(bodyName);
                else
                    r030Fix1CanonicalEnvironment[bodyName] = environmentHash;
            }
        }

        internal static void ClearR030Fix1CanonicalEnvironment(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (environmentSync) r030Fix1CanonicalEnvironment.Remove(bodyName);
        }

        internal static string PersistentTerrainIdentityForBody(CelestialBody body)
        {
            if (body == null || string.IsNullOrEmpty(body.name)) return string.Empty;
            string cacheKey = body.name + "|" +
                body.Radius.ToString("R", CultureInfo.InvariantCulture) + "|" +
                (body.ocean ? "1" : "0");
            lock (environmentSync)
            {
                string cached;
                if (r030Fix1PersistentIdentityCache.TryGetValue(cacheKey, out cached))
                    return cached;
            }
            var builder = new System.Text.StringBuilder(4096);
            builder.Append("AERIS_R030_PERSISTENT_ID_V1|")
                .Append(AERISTerrainTileFormat.Version).Append('|')
                .Append(AERISTerrainPreloadFormat.DatabaseFormatVersion).Append('|')
                .Append(body.name).Append('|')
                .Append(body.Radius.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(body.ocean ? "1" : "0").Append('|');
            AppendPqsStructuralFingerprint(builder, body);
            string result = AERISTerrainHash.Fnv1A64Hex(builder.ToString());
            lock (environmentSync) r030Fix1PersistentIdentityCache[cacheKey] = result;
            return result;
        }

        internal static string TerrainWitnessHashForBody(CelestialBody body)
        {
            if (body == null || string.IsNullOrEmpty(body.name) ||
                !BodyHasSolidSurface(body)) return string.Empty;
            string identity = PersistentTerrainIdentityForBody(body);
            if (string.IsNullOrEmpty(identity)) return string.Empty;
            string cacheKey = body.name + "|" + identity;
            lock (environmentSync)
            {
                string cached;
                if (r030Fix1WitnessCache.TryGetValue(cacheKey, out cached))
                    return cached;
            }

            // Twelve fixed, globally distributed witnesses are intentionally sparse:
            // this is a startup identity guard, not a terrain producer. Elevation is
            // quantized to centimetres to reject meaningless floating-point noise while
            // remaining far tighter than any ND/LAND terrain requirement.
            double[,] points = new double[,]
            {
                { -67.5, -157.5 }, { -52.5,  -82.5 }, { -37.5,   -7.5 },
                { -22.5,   67.5 }, {  -7.5,  142.5 }, {   7.5, -142.5 },
                {  22.5,  -67.5 }, {  37.5,    7.5 }, {  52.5,   82.5 },
                {  67.5,  157.5 }, {  11.25,  33.75 }, { -11.25, -146.25 }
            };
            var builder = new System.Text.StringBuilder(512);
            builder.Append("AERIS_R030_WITNESS_V1|").Append(identity).Append('|');
            try
            {
                for (int i = 0; i < points.GetLength(0); i++)
                {
                    double elevation;
                    double latitude = points[i, 0];
                    double longitude = points[i, 1];
                    if (!AERISTerrainAwareness.TrySampleTerrainAslShared(body,
                        latitude, longitude, out elevation) ||
                        double.IsNaN(elevation) || double.IsInfinity(elevation))
                        return string.Empty;
                    long centimetres = (long)Math.Round(elevation * 100.0,
                        MidpointRounding.AwayFromZero);
                    builder.Append(latitude.ToString("R", CultureInfo.InvariantCulture))
                        .Append(',')
                        .Append(longitude.ToString("R", CultureInfo.InvariantCulture))
                        .Append('=').Append(centimetres).Append(';');
                }
            }
            catch
            {
                return string.Empty;
            }
            string result = AERISTerrainHash.Fnv1A64Hex(builder.ToString());
            lock (environmentSync) r030Fix1WitnessCache[cacheKey] = result;
            return result;
        }

        static void AppendPqsStructuralFingerprint(System.Text.StringBuilder builder,
            CelestialBody body)
        {
            if (builder == null || body == null) return;
            try
            {
                FieldInfo field = body.GetType().GetField("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo property = body.GetType().GetProperty("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object pqs = field != null ? field.GetValue(body) :
                    property == null ? null : property.GetValue(body, null);
                if (pqs == null)
                {
                    builder.Append("NO_PQS|");
                    return;
                }
                builder.Append("PQS:").Append(pqs.GetType().AssemblyQualifiedName).Append('|');
                object mods = ReadMemberValue(pqs, "mods");
                System.Collections.IEnumerable enumerable =
                    mods as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    builder.Append("NO_MOD_ENUM|");
                    return;
                }
                int count = 0;
                foreach (object mod in enumerable)
                {
                    if (mod == null || count++ >= 128) break;
                    builder.Append("MOD:").Append(mod.GetType().AssemblyQualifiedName)
                        .Append('|');
                }
                builder.Append("MOD_COUNT=").Append(count).Append('|');
            }
            catch (Exception ex)
            {
                builder.Append("PQS_STRUCT_ERROR:")
                    .Append(ex.GetType().FullName).Append('|');
            }
        }

''' + anchor
tile = replace_once(tile, anchor, insert, 'tile persistent identity methods')

old = '''        internal static string EnvironmentHashForBody(CelestialBody body)
        {
            if (!GameDataHashReady) return string.Empty;
            string cacheKey = (body == null ? string.Empty : body.name) + "|" +
'''
new = '''        internal static string EnvironmentHashForBody(CelestialBody body)
        {
            if (!GameDataHashReady) return string.Empty;
            string canonicalBodyName = body == null ? string.Empty : body.name;
            if (!string.IsNullOrEmpty(canonicalBodyName))
            {
                lock (environmentSync)
                {
                    string canonical;
                    if (r030Fix1CanonicalEnvironment.TryGetValue(canonicalBodyName,
                        out canonical) && !string.IsNullOrEmpty(canonical))
                        return canonical;
                }
            }
            string cacheKey = (body == null ? string.Empty : body.name) + "|" +
'''
tile = replace_once(tile, old, new, 'tile canonical environment intercept')
TILE.write_text(tile)

# ---------------------------------------------------------------------------
# Builder: V4 -> V5 compatible persistence, startup point-signature adoption,
# and canonical reuse only after persistent identity/witness validation.
# ---------------------------------------------------------------------------
old = '''            internal string EnvironmentHash = string.Empty;
            internal long Generation = 1L;
'''
new = '''            internal string EnvironmentHash = string.Empty;
            internal string PersistentTerrainIdentity = string.Empty;
            internal string TerrainWitnessHash = string.Empty;
            internal long Generation = 1L;
'''
builder = replace_once(builder, old, new, 'builder body persistent fields')

old = '''        string appliedPointSetSignature = string.Empty;
        AERISTerrainPreloadMode mode;
'''
new = '''        string appliedPointSetSignature = string.Empty;
        int loadedStateVersion;
        bool legacyPointSignatureAdoptionPending;
        string legacyPointSignatureCandidate = string.Empty;
        float legacyPointSignatureCandidateSince;
        AERISTerrainPreloadMode mode;
'''
builder = replace_once(builder, old, new, 'builder state-version fields')

old = '''                // A non-Flight registry refresh is now authoritative. If Flight churn
                // returned to the exact point-set that was already completed, clear the
                // deferred signal without rebuilding anything. Otherwise invalidate once.
'''
new = '''                // R030 Fix1 V4 migration: the old state format never persisted the
                // applied point-set signature. On the first authoritative non-Flight
                // snapshot there is therefore no historical signature to compare against.
                // Adopt a signature only after it has stayed stable for five seconds, so
                // a transient empty/partial startup registry cannot become the baseline.
                if (legacyPointSignatureAdoptionPending)
                {
                    string candidate = pointSetSignature ?? string.Empty;
                    float now = Time.realtimeSinceStartup;
                    if (!string.Equals(legacyPointSignatureCandidate, candidate,
                        StringComparison.Ordinal))
                    {
                        legacyPointSignatureCandidate = candidate;
                        legacyPointSignatureCandidateSince = now;
                        return;
                    }
                    if (now - legacyPointSignatureCandidateSince < 5.0f)
                        return;
                    appliedPointSetSignature = candidate;
                    legacyPointSignatureAdoptionPending = false;
                    deferredPointSetInvalidation = false;
                    stateDirty = true;
                    AERISLogger.Info("[R030_FIX1][POINTS] event=ADOPT_V4_BASELINE" +
                        "; signature=" + appliedPointSetSignature +
                        "; stable_seconds=" +
                        (now - legacyPointSignatureCandidateSince).ToString(
                            "0.0", CultureInfo.InvariantCulture) +
                        "; bodies=" + plans.Count);
                    return;
                }

                // A non-Flight registry refresh is now authoritative. If Flight churn
                // returned to the exact point-set that was already completed, clear the
                // deferred signal without rebuilding anything. Otherwise invalidate once.
'''
builder = replace_once(builder, old, new, 'builder V4 point signature adoption')

old = '''        void EnsureEnvironment(BodyPlan plan, CelestialBody body)
        {
            string environment = AERISTerrainTileSystem.EnvironmentHashForBody(body);
            if (string.Equals(plan.EnvironmentHash, environment,
                StringComparison.Ordinal)) return;
            plan.EnvironmentHash = environment;
            InvalidateAutomaticCompletion(plan);
            InvalidateCoastlineCompletion(plan);
            plan.Generation++;
            ResetScanState(plan);
            stateDirty = true;
            string validationKey = body.name + "|" + environment;
            if (validatedEnvironments.Contains(validationKey)) return;
            validatedEnvironments.Add(validationKey);
            ScheduleEnvironmentInvalidation(body.name, environment);
        }
'''
new = '''        void EnsureEnvironment(BodyPlan plan, CelestialBody body)
        {
            if (plan == null || body == null) return;

            string persistentIdentity =
                AERISTerrainTileSystem.PersistentTerrainIdentityForBody(body);
            string witness = AERISTerrainTileSystem.TerrainWitnessHashForBody(body);
            bool haveStoredIdentity =
                !string.IsNullOrEmpty(plan.PersistentTerrainIdentity) &&
                !string.IsNullOrEmpty(plan.TerrainWitnessHash);
            bool stableIdentityMatches = haveStoredIdentity &&
                string.Equals(plan.PersistentTerrainIdentity, persistentIdentity,
                    StringComparison.Ordinal) &&
                string.Equals(plan.TerrainWitnessHash, witness,
                    StringComparison.Ordinal);
            bool haveCanonical = !string.IsNullOrEmpty(plan.EnvironmentHash);
            bool canonicalPayloadExists = false;

            // A V5 state can resume both complete and partial work if its stable identity
            // and witness still match. A V4 state lacks those fields, so the one-time
            // migration trusts only a fully READY body whose current canonical environment
            // still has indexed complete payloads. This preserves the user's completed DB
            // without turning an unknown partial legacy state into authority.
            bool v5Reusable = loadedStateVersion >= 5 && stableIdentityMatches &&
                haveCanonical;
            bool v4Ready = loadedStateVersion == 4 && plan.AutomaticComplete &&
                plan.CoastlineComplete &&
                !string.IsNullOrEmpty(plan.CompletedEnvironmentHash) &&
                string.Equals(plan.CompletedEnvironmentHash, plan.EnvironmentHash,
                    StringComparison.Ordinal) &&
                string.Equals(plan.CompletedCoastlineEnvironmentHash,
                    plan.EnvironmentHash, StringComparison.Ordinal);
            if (v4Ready && database != null && haveCanonical &&
                !string.IsNullOrEmpty(persistentIdentity) &&
                !string.IsNullOrEmpty(witness))
            {
                AERISTerrainTileKey[] existing =
                    database.SnapshotCompleteKeysForBody(plan.BodyName,
                        plan.EnvironmentHash);
                canonicalPayloadExists = existing != null && existing.Length > 0;
            }
            bool v4Migrate = v4Ready && canonicalPayloadExists;

            if (v5Reusable || v4Migrate)
            {
                AERISTerrainTileSystem.SetR030Fix1CanonicalEnvironment(
                    body.name, plan.EnvironmentHash);
                if (v4Migrate)
                {
                    plan.PersistentTerrainIdentity = persistentIdentity;
                    plan.TerrainWitnessHash = witness;
                    stateDirty = true;
                    AERISLogger.Info("[R030_FIX1][MIGRATE] body=" + body.name +
                        "; event=V4_CANONICAL_ADOPTED; environment=" +
                        plan.EnvironmentHash + "; identity=" + persistentIdentity +
                        "; witness=" + witness + "; indexed_payload=true");
                }
            }
            else
            {
                AERISTerrainTileSystem.ClearR030Fix1CanonicalEnvironment(body.name);
            }

            string environment = AERISTerrainTileSystem.EnvironmentHashForBody(body);
            if (string.Equals(plan.EnvironmentHash, environment,
                StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(persistentIdentity) &&
                    !string.Equals(plan.PersistentTerrainIdentity, persistentIdentity,
                        StringComparison.Ordinal))
                {
                    plan.PersistentTerrainIdentity = persistentIdentity;
                    stateDirty = true;
                }
                if (!string.IsNullOrEmpty(witness) &&
                    !string.Equals(plan.TerrainWitnessHash, witness,
                        StringComparison.Ordinal))
                {
                    plan.TerrainWitnessHash = witness;
                    stateDirty = true;
                }
                return;
            }

            string previousEnvironment = plan.EnvironmentHash ?? string.Empty;
            string previousIdentity = plan.PersistentTerrainIdentity ?? string.Empty;
            string previousWitness = plan.TerrainWitnessHash ?? string.Empty;
            plan.PersistentTerrainIdentity = persistentIdentity ?? string.Empty;
            plan.TerrainWitnessHash = witness ?? string.Empty;
            plan.EnvironmentHash = environment;
            InvalidateAutomaticCompletion(plan);
            InvalidateCoastlineCompletion(plan);
            plan.Generation++;
            ResetScanState(plan);
            stateDirty = true;
            AERISLogger.Warn("[R030_FIX1][INVALIDATE] body=" + body.name +
                "; previous_environment=" + previousEnvironment +
                "; new_environment=" + environment +
                "; identity_match=" +
                string.Equals(previousIdentity, persistentIdentity,
                    StringComparison.Ordinal) +
                "; witness_match=" +
                string.Equals(previousWitness, witness, StringComparison.Ordinal) +
                "; witness_available=" + !string.IsNullOrEmpty(witness));

            string validationKey = body.name + "|" + environment;
            if (validatedEnvironments.Contains(validationKey)) return;
            validatedEnvironments.Add(validationKey);
            ScheduleEnvironmentInvalidation(body.name, environment);
        }
'''
builder = replace_once(builder, old, new, 'builder stable EnsureEnvironment')

old = '''            AERISTerrainPreloadMode loadedMode;
            try
'''
new = '''            AERISTerrainPreloadMode loadedMode;
            int loadedVersion = 0;
            string loadedAppliedPointSetSignature = string.Empty;
            try
'''
builder = replace_once(builder, old, new, 'builder state load locals')

old = '''                    int version = reader.ReadInt32();
                    if (version != 4) return false;
                    loadedMode = (AERISTerrainPreloadMode)reader.ReadInt32();
'''
new = '''                    int version = reader.ReadInt32();
                    if (version != 4 && version != 5) return false;
                    loadedVersion = version;
                    loadedMode = (AERISTerrainPreloadMode)reader.ReadInt32();
                    if (version >= 5)
                        loadedAppliedPointSetSignature = reader.ReadString();
'''
builder = replace_once(builder, old, new, 'builder V4/V5 reader header')

old = '''                        plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        // Candidate 13 removes all per-body preload tuning. Legacy
'''
new = '''                        plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        if (version >= 5)
                        {
                            plan.PersistentTerrainIdentity = reader.ReadString();
                            plan.TerrainWitnessHash = reader.ReadString();
                        }
                        // Candidate 13 removes all per-body preload tuning. Legacy
'''
builder = replace_once(builder, old, new, 'builder V5 body reader fields')

old = '''                speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;
            }
            return true;
'''
new = '''                speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;
                loadedStateVersion = loadedVersion;
                appliedPointSetSignature = loadedAppliedPointSetSignature ?? string.Empty;
                pointSetSignature = appliedPointSetSignature;
                legacyPointSignatureAdoptionPending = loadedVersion == 4;
                AERISLogger.Info("[R030_FIX1][STATE] event=LOAD; version=" +
                    loadedStateVersion + "; plans=" + plans.Count +
                    "; applied_point_signature=" + appliedPointSetSignature +
                    "; v4_adoption_pending=" + legacyPointSignatureAdoptionPending);
            }
            return true;
'''
builder = replace_once(builder, old, new, 'builder loaded state activation')

old = '''                        CompletedCoastlineEnvironmentHash =
                            plan.CompletedCoastlineEnvironmentHash
                    });
'''
new = '''                        CompletedCoastlineEnvironmentHash =
                            plan.CompletedCoastlineEnvironmentHash,
                        PersistentTerrainIdentity =
                            plan.PersistentTerrainIdentity,
                        TerrainWitnessHash = plan.TerrainWitnessHash
                    });
'''
builder = replace_once(builder, old, new, 'builder state snapshot new fields')

old = '''        bool WriteStateSnapshot(IList<BodyPlan> snapshot,
            AERISTerrainPreloadMode snapshotMode)
        {
            try
'''
new = '''        bool WriteStateSnapshot(IList<BodyPlan> snapshot,
            AERISTerrainPreloadMode snapshotMode)
        {
            string snapshotAppliedPointSetSignature;
            lock (sync) snapshotAppliedPointSetSignature =
                appliedPointSetSignature ?? string.Empty;
            return WriteStateSnapshot(snapshot, snapshotMode,
                snapshotAppliedPointSetSignature);
        }

        bool WriteStateSnapshot(IList<BodyPlan> snapshot,
            AERISTerrainPreloadMode snapshotMode,
            string snapshotAppliedPointSetSignature)
        {
            try
'''
builder = replace_once(builder, old, new, 'builder V5 writer overload')

old = '''                    writer.Write(AERISTerrainPreloadFormat.StateMagic);
                    writer.Write(4);
                    writer.Write((int)snapshotMode);
                    writer.Write(snapshot == null ? 0 : snapshot.Count);
'''
new = '''                    writer.Write(AERISTerrainPreloadFormat.StateMagic);
                    writer.Write(5);
                    writer.Write((int)snapshotMode);
                    writer.Write(snapshotAppliedPointSetSignature ?? string.Empty);
                    writer.Write(snapshot == null ? 0 : snapshot.Count);
'''
builder = replace_once(builder, old, new, 'builder V5 writer header')

old = '''                            writer.Write(plan.CompletedCoastlineEnvironmentHash ??
                                string.Empty);
'''
new = '''                            writer.Write(plan.CompletedCoastlineEnvironmentHash ??
                                string.Empty);
                            writer.Write(plan.PersistentTerrainIdentity ?? string.Empty);
                            writer.Write(plan.TerrainWitnessHash ?? string.Empty);
'''
builder = replace_once(builder, old, new, 'builder V5 writer body fields')

old = '''    internal sealed class AERISTerrainPreloadBuilder : IDisposable
    {
'''
new = '''    internal sealed class AERISTerrainPreloadBuilder : IDisposable
    {
        // AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY
'''
builder = replace_once(builder, old, new, 'builder Fix1 marker')
BUILDER.write_text(builder)

# ---------------------------------------------------------------------------
# Fix1 observer: understands both V4 and V5 and reports stable identity/witness.
# ---------------------------------------------------------------------------
OBSERVER_TEXT = r'''using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR030Fix1PersistenceObserver : MonoBehaviour
    {
        sealed class Plan
        {
            internal string BodyName = string.Empty;
            internal bool AutomaticComplete;
            internal string CompletedEnvironmentHash = string.Empty;
            internal string EnvironmentHash = string.Empty;
            internal bool CoastlineComplete;
            internal string CompletedCoastlineEnvironmentHash = string.Empty;
            internal string PersistentIdentity = string.Empty;
            internal string Witness = string.Empty;
        }

        float nextAttempt;
        bool captured;

        void Update()
        {
            if (captured || Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (!AERISTerrainTileSystem.GameDataHashReady ||
                FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            Capture();
            captured = true;
        }

        void Capture()
        {
            string dbRoot = Path.Combine(KSPUtil.ApplicationRootPath ?? string.Empty,
                "GameData", "AERISFlightControl", "PluginData",
                "TerrainPreloadDatabaseV3");
            string path = Path.Combine(dbRoot, "preload_state.aps");
            if (!File.Exists(path) && File.Exists(path + ".bak")) path += ".bak";

            int version;
            string pointSignature;
            string stateStatus;
            Dictionary<string, Plan> plans =
                ReadState(path, out version, out pointSignature, out stateStatus);
            int solid = 0;
            int ready = 0;
            int identityMatches = 0;
            int witnessMatches = 0;
            int reusable = 0;

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name) ||
                    !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                solid++;

                string identity =
                    AERISTerrainTileSystem.PersistentTerrainIdentityForBody(body);
                string witness =
                    AERISTerrainTileSystem.TerrainWitnessHashForBody(body);
                Plan plan;
                bool present = plans.TryGetValue(body.name, out plan) && plan != null;
                bool idMatch = present && !string.IsNullOrEmpty(plan.PersistentIdentity) &&
                    string.Equals(plan.PersistentIdentity, identity,
                        StringComparison.Ordinal);
                bool witnessMatch = present && !string.IsNullOrEmpty(plan.Witness) &&
                    string.Equals(plan.Witness, witness, StringComparison.Ordinal);
                bool stateReady = present && plan.AutomaticComplete &&
                    plan.CoastlineComplete &&
                    string.Equals(plan.CompletedEnvironmentHash,
                        plan.EnvironmentHash, StringComparison.Ordinal) &&
                    string.Equals(plan.CompletedCoastlineEnvironmentHash,
                        plan.EnvironmentHash, StringComparison.Ordinal);
                if (stateReady) ready++;
                if (idMatch) identityMatches++;
                if (witnessMatch) witnessMatches++;
                if (present && !string.IsNullOrEmpty(plan.EnvironmentHash) &&
                    idMatch && witnessMatch) reusable++;

                AERISLogger.Info("[R030_FIX1][BODY] body=" + body.name +
                    "; state_present=" + present +
                    "; state_ready=" + stateReady +
                    "; environment=" + (present ? plan.EnvironmentHash : string.Empty) +
                    "; persistent_identity=" +
                        (present ? plan.PersistentIdentity : string.Empty) +
                    "; current_identity=" + identity +
                    "; identity_match=" + idMatch +
                    "; stored_witness=" + (present ? plan.Witness : string.Empty) +
                    "; current_witness=" + witness +
                    "; witness_match=" + witnessMatch +
                    "; reusable=" + (present && !string.IsNullOrEmpty(
                        plan.EnvironmentHash) && idMatch && witnessMatch));
            }

            AERISLogger.Info("[R030_FIX1][SUMMARY] state=" + stateStatus +
                "; version=" + version +
                "; applied_point_signature=" + pointSignature +
                "; solid=" + solid +
                "; plans=" + plans.Count +
                "; ready=" + ready +
                "; identity_match=" + identityMatches +
                "; witness_match=" + witnessMatches +
                "; reusable=" + reusable +
                "; manifest=" + File.Exists(Path.Combine(dbRoot, "manifest.atm")));
        }

        static Dictionary<string, Plan> ReadState(string path, out int version,
            out string pointSignature, out string status)
        {
            version = 0;
            pointSignature = string.Empty;
            var result = new Dictionary<string, Plan>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                status = "ABSENT";
                return result;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(stream))
                {
                    string magic = reader.ReadString();
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
                    if (count < 0 || count > 4096)
                        throw new InvalidDataException("state body count");
                    for (int i = 0; i < count; i++)
                    {
                        var plan = new Plan();
                        plan.BodyName = reader.ReadString();
                        reader.ReadInt32();
                        reader.ReadBoolean();
                        reader.ReadInt32();
                        reader.ReadBoolean();
                        reader.ReadBoolean();
                        plan.AutomaticComplete = reader.ReadBoolean();
                        reader.ReadInt32();
                        reader.ReadBoolean();
                        plan.CompletedEnvironmentHash = reader.ReadString();
                        reader.ReadInt64();
                        reader.ReadInt64();
                        reader.ReadInt64();
                        reader.ReadInt64();
                        reader.ReadInt64();
                        reader.ReadInt32();
                        plan.EnvironmentHash = reader.ReadString();
                        reader.ReadBoolean();
                        reader.ReadInt64();
                        plan.CoastlineComplete = reader.ReadBoolean();
                        reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        if (version >= 5)
                        {
                            plan.PersistentIdentity = reader.ReadString();
                            plan.Witness = reader.ReadString();
                        }
                        if (!string.IsNullOrEmpty(plan.BodyName))
                            result[plan.BodyName] = plan;
                    }
                    status = stream.Position == stream.Length ?
                        "V" + version + "_OK" :
                        "V" + version + "_TRAILING_DATA";
                }
            }
            catch (Exception ex)
            {
                status = "READ_FAIL:" + ex.GetType().Name;
                result.Clear();
            }
            return result;
        }
    }
}
'''
OBSERVER.parent.mkdir(parents=True, exist_ok=True)
if OBSERVER.exists() and OBSERVER.read_text() != OBSERVER_TEXT:
    raise SystemExit('R030 Fix1 observer exists with unexpected content')
OBSERVER.write_text(OBSERVER_TEXT)

csproj = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR030Fix1PersistenceObserver.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISR030PreloadPersistencePtcPhase0Observer.cs" />\n'
if include not in csproj:
    if anchor not in csproj:
        raise SystemExit('R030 Fix1 csproj base observer anchor missing')
    csproj = csproj.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(csproj)

version_text = VERSION.read_text()
version_text = version_text.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R030 PRELOAD PERSISTENCE + PTC PHASE0 OBSERVER',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R030 FIX1 STABLE PERSISTENT TERRAIN IDENTITY')
version_text = version_text.replace(
    'DEV CP3.75 — AERIS32 — REV3.5 R030 — PRELOAD PERSISTENCE + PTC PHASE0 OBSERVER',
    'DEV CP3.75 — AERIS32 — REV3.5 R030 FIX1 — STABLE PERSISTENT TERRAIN IDENTITY')
version_text = version_text.replace(
    'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0',
    'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY')
source_hash = tree_hash()
version_text = re.sub(
    r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + source_hash + '";',
    version_text)
VERSION.write_text(version_text)

final_builder = BUILDER.read_text()
final_tile = TILE.read_text()
checks = (
    (MARKER in final_builder, 'builder marker'),
    (MARKER in final_tile, 'tile marker'),
    ('writer.Write(5);' in final_builder, 'state V5 writer'),
    ('version != 4 && version != 5' in final_builder, 'V4/V5 reader'),
    ('PersistentTerrainIdentityForBody(body)' in final_builder,
        'stable identity validation'),
    ('TerrainWitnessHashForBody(body)' in final_builder,
        'witness validation'),
    ('SetR030Fix1CanonicalEnvironment' in final_builder,
        'canonical reuse registration'),
    ('ADOPT_V4_BASELINE' in final_builder, 'point-set V4 migration'),
    ('r030Fix1CanonicalEnvironment.TryGetValue' in final_tile,
        'canonical intercept'),
)
failed = [label for ok, label in checks if not ok]
if failed:
    raise SystemExit('R030 Fix1 materialization FAIL: ' + ', '.join(failed))

print('PASS: materialized R030 Fix1 stable persistent terrain identity')
print('runtime_change=PERSISTENCE_FIX_ONLY')
print('state_format=V5_READ_V4_V5')
print('legacy_v4_policy=READY_CANONICAL_ADOPTION_WITH_INDEXED_PAYLOAD')
print('point_signature=V5_PERSISTED_V4_FIRST_NONFLIGHT_ADOPTION')
print('terrain_identity=STRUCTURAL_PQS_GRAPH_PLUS_12_POINT_CM_WITNESS')
print('builder_sha256=' + sha256(BUILDER))
print('tile_sha256=' + sha256(TILE))
print('observer_sha256=' + sha256(OBSERVER))
print('source_tree_sha256=' + source_hash)
