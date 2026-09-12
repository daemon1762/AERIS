using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 4B main-thread runtime snapshot builder.
    //
    // This class is deliberately capture-only. It reads live KSP/PQS setup state on
    // the main thread and returns the immutable Phase 4A worker payload. It never
    // invokes terrain callbacks, never writes runtime state, never writes the terrain
    // DB and never authorizes a producer switch.
    //
    // R041 family certification is fail-closed against the exact modifier topology
    // observed by the final accepted R041 V5 runtime witness. Any additional, removed,
    // reordered or reconfigured unsupported modifier returns failure and leaves PQS as
    // the only production authority.
    internal static class AERISR042ExactCpuShadowRuntimeSnapshotBuilder
    {
        internal const int CertifiedBodyCount = 7;
        internal const int R041CertifiedBodyCount = 6;

        sealed class ModRecord
        {
            internal PQSMod Mod;
            internal string TypeName;
            internal string ShortTypeName;
            internal int Order;
            internal int Index;
        }

        sealed class CurveSelection
        {
            internal AERISR041MohoDresPureCpuExact.CurveSnapshot Snapshot;
            internal AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedCurveEvaluationMode RidgedMode;
            internal bool Exact;
            internal int Matches;
            internal int Tests;
            internal double MaxAbsError;
        }

        internal static bool TryCapture(
            CelestialBody body,
            int mainThreadId,
            out AERISR042ExactCpuShadowRuntimeSnapshot snapshot,
            out string failure)
        {
            snapshot = null;
            failure = string.Empty;

            try
            {
                if (mainThreadId <= 0)
                {
                    failure = "MAIN_THREAD_ID_INVALID";
                    return false;
                }

                int captureThreadId = Thread.CurrentThread.ManagedThreadId;
                if (captureThreadId != mainThreadId)
                {
                    failure = "CAPTURE_NOT_ON_MAIN_THREAD";
                    return false;
                }

                AERISR042ExactCpuShadowSourceResolver.Decision decision =
                    AERISR042ExactCpuShadowSourceResolver.ResolveCandidate(body);
                if (decision == null || !decision.IsCandidate)
                {
                    failure = decision == null
                        ? "SOURCE_RESOLVER_NULL"
                        : "SOURCE_RESOLVER_FALLBACK:" + decision.FallbackReason;
                    return false;
                }
                if (!decision.RuntimeSnapshotCertificationRequired)
                {
                    failure = "RUNTIME_SNAPSHOT_GATE_NOT_REQUIRED_UNEXPECTED";
                    return false;
                }

                if (body == null || body.pqsController == null)
                {
                    failure = "PQS_MISSING";
                    return false;
                }

                object pqs = body.pqsController;
                double pqsRadius = ReadDouble(pqs, "radius");
                double radiusMin = ReadDouble(pqs, "radiusMin");
                string topology;
                string environmentHash;

                if (decision.Candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R039MinmusVertexPlanet)
                {
                    AERISR039MinmusPureCpuExact.VertexPlanetSnapshot minmus;
                    string minmusFailure;
                    if (!AERISR039PtcMinmusPureCpuExactValidationObserver.
                        TryCreateR040BProductionSnapshot(body, out minmus, out minmusFailure))
                    {
                        failure = "R039_MINMUS_CAPTURE:" + minmusFailure;
                        return false;
                    }

                    topology = "PQSMod_VertexPlanet:R039_ACCEPTED_MAIN_IL";
                    environmentHash = BuildEnvironmentHash(
                        decision.Certificate,
                        body.name,
                        topology,
                        pqsRadius,
                        radiusMin);

                    snapshot = new AERISR042ExactCpuShadowRuntimeSnapshot(
                        body.name,
                        decision.Candidate,
                        decision.Certificate,
                        environmentHash,
                        topology,
                        captureThreadId,
                        pqsRadius,
                        minmus,
                        null);
                    return snapshot.IsStructurallyValid;
                }

                if (decision.Candidate !=
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain)
                {
                    failure = "UNSUPPORTED_CANDIDATE_KIND";
                    return false;
                }

                AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot chain;
                if (!TryCaptureR041HeightChain(
                    body,
                    out chain,
                    out topology,
                    out failure))
                    return false;

                environmentHash = BuildEnvironmentHash(
                    decision.Certificate,
                    body.name,
                    topology,
                    pqsRadius,
                    radiusMin);

                snapshot = new AERISR042ExactCpuShadowRuntimeSnapshot(
                    body.name,
                    decision.Candidate,
                    decision.Certificate,
                    environmentHash,
                    topology,
                    captureThreadId,
                    pqsRadius,
                    null,
                    chain);

                if (!snapshot.IsStructurallyValid)
                {
                    snapshot = null;
                    failure = "RUNTIME_SNAPSHOT_STRUCTURALLY_INVALID";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                snapshot = null;
                failure = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
                return false;
            }
        }

        static bool TryCaptureR041HeightChain(
            CelestialBody body,
            out AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot chain,
            out string topology,
            out string failure)
        {
            chain = null;
            topology = string.Empty;
            failure = string.Empty;

            if (body == null || body.pqsController == null)
            {
                failure = "R041_PQS_MISSING";
                return false;
            }

            string bodyName = body.name ?? string.Empty;
            string expectedTopology = ExpectedR041Topology(bodyName);
            if (string.IsNullOrEmpty(expectedTopology))
            {
                failure = "R041_BODY_NOT_CERTIFIED:" + bodyName;
                return false;
            }

            object pqs = body.pqsController;
            double radiusMin = ReadDouble(pqs, "radiusMin");
            double radiusDelta = ReadDouble(pqs, "radiusDelta");
            double[] randomVectors = SnapshotLibNoiseRandomVectors();
            List<ModRecord> mods = CollectHeightMods(pqs);
            if (mods.Count == 0)
            {
                failure = "R041_NO_ENABLED_HEIGHT_MODIFIERS";
                return false;
            }

            topology = Topology(mods);
            string semanticTopology = SemanticTopology(mods);
            string expectedSemanticTopology =
                SemanticTopologyFromLegacyExpected(expectedTopology);
            if (!string.Equals(
                semanticTopology, expectedSemanticTopology,
                StringComparison.Ordinal))
            {
                failure = "R041_TOPOLOGY_MISMATCH:expected=" + expectedTopology +
                    ":actual=" + topology +
                    ":expected_semantic=" + expectedSemanticTopology +
                    ":actual_semantic=" + semanticTopology;
                return false;
            }

            var pureOps = new AERISR041MohoDresPureCpuExact.HeightOpSnapshot[mods.Count];

            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                switch (record.ShortTypeName)
                {
                    case "PQSMod_VertexHeightMap":
                    {
                        MapSO map = RequireMember(record.Mod, "heightMap") as MapSO;
                        if (map == null)
                            throw new InvalidOperationException(bodyName + "_HEIGHTMAP_MAPSO_MISSING");
                        AERIS39MapSoPureCpuExact.MapSnapshot mapSnapshot = SnapshotMap(map);
                        var heightMap = new AERIS39HeightMapPureCpuExact.Snapshot(
                            ReadDouble(record.Mod, "heightMapOffset"),
                            ReadDouble(record.Mod, "heightMapDeformity"),
                            mapSnapshot);
                        pureOps[i] =
                            new AERIS39AllBodyHeightModifierChainPureCpuExact.CertifiedHeightMapOpSnapshot(
                                heightMap);
                        break;
                    }

                    case "PQSMod_VertexSimplexHeight":
                        pureOps[i] = new AERISR041MohoDresPureCpuExact.SimplexHeightOpSnapshot(
                            ReadDouble(record.Mod, "deformity"),
                            SnapshotSimplex(RequireMember(record.Mod, "simplex")));
                        break;

                    case "PQSMod_FlattenOcean":
                        pureOps[i] = new AERISR041MohoDresPureCpuExact.FlattenOceanOpSnapshot(
                            ReadDouble(record.Mod, "oceanRad"));
                        break;

                    case "PQSMod_VertexHeightNoiseVertHeightCurve2":
                    {
                        AnimationCurve curve = RequireMember(record.Mod, "simplexCurve") as AnimationCurve;
                        if (curve == null)
                            throw new InvalidOperationException(bodyName + "_CURVE_MISSING");
                        CurveSelection selection = SelectCurveSnapshot(bodyName, curve);
                        if (!selection.Exact)
                            throw new InvalidOperationException(bodyName + "_ANIMATIONCURVE_NOT_BIT_EXACT");

                        pureOps[i] = new AERISR041MohoDresPureCpuExact.Curve2OpSnapshot(
                            Convert.ToSingle(
                                RequireMember(record.Mod, "deformity"),
                                CultureInfo.InvariantCulture),
                            radiusMin,
                            ReadDouble(record.Mod, "simplexHeightStart"),
                            ReadDouble(record.Mod, "simplexHeightEnd"),
                            ReadDouble(record.Mod, "hDeltaR"),
                            SnapshotSimplex(RequireMember(record.Mod, "simplex")),
                            SnapshotRidged(RequireMember(record.Mod, "ridgedAdd"), randomVectors),
                            SnapshotRidged(RequireMember(record.Mod, "ridgedSub"), randomVectors),
                            selection.Snapshot);
                        break;
                    }

                    case "PQSMod_VertexRidgedAltitudeCurve":
                    {
                        AnimationCurve curve = RequireMember(record.Mod, "simplexCurve") as AnimationCurve;
                        if (curve == null)
                            throw new InvalidOperationException(bodyName + "_RIDGED_ALTITUDE_CURVE_MISSING");
                        CurveSelection selection = SelectRidgedCurveSnapshot(bodyName, curve);
                        if (!selection.Exact)
                            throw new InvalidOperationException(
                                bodyName + "_RIDGED_ALTITUDE_ANIMATIONCURVE_NOT_BIT_EXACT");

                        pureOps[i] =
                            new AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedAltitudeCurveOpSnapshot(
                                Convert.ToSingle(
                                    RequireMember(record.Mod, "deformity"),
                                    CultureInfo.InvariantCulture),
                                radiusMin,
                                Convert.ToSingle(
                                    RequireMember(record.Mod, "ridgedMinimum"),
                                    CultureInfo.InvariantCulture),
                                ReadDouble(record.Mod, "simplexHeightStart"),
                                ReadDouble(record.Mod, "simplexHeightEnd"),
                                ReadDouble(record.Mod, "hDeltaR"),
                                SnapshotSimplex(RequireMember(record.Mod, "simplex")),
                                SnapshotRidged(RequireMember(record.Mod, "ridgedAdd"), randomVectors),
                                selection.Snapshot,
                                selection.RidgedMode);
                        break;
                    }

                    case "PQSMod_VertexSimplexHeightAbsolute":
                        pureOps[i] = new AERISR041MohoDresPureCpuExact.SimplexAbsoluteOpSnapshot(
                            ReadDouble(record.Mod, "deformity"),
                            SnapshotSimplex(RequireMember(record.Mod, "simplex")));
                        break;

                    case "PQSMod_VertexHeightNoise":
                    {
                        object noise = RequireMember(record.Mod, "noiseMap");
                        if (!string.Equals(
                            TypeName(noise.GetType()),
                            "LibNoise.RidgedMultifractal",
                            StringComparison.Ordinal))
                            throw new InvalidOperationException(bodyName + "_HEIGHT_NOISE_NOT_RIDGED");

                        pureOps[i] = new AERISR041MohoDresPureCpuExact.HeightNoiseRidgedOpSnapshot(
                            Convert.ToSingle(
                                RequireMember(record.Mod, "deformity"),
                                CultureInfo.InvariantCulture),
                            SnapshotRidged(noise, randomVectors));
                        break;
                    }

                    case "PQSMod_MapDecalTangent":
                    {
                        MapSO map = ReadMember(record.Mod, "heightMap") as MapSO;
                        AERIS39MapSoPureCpuExact.MapSnapshot mapSnapshot =
                            map == null ? null : SnapshotMap(map);
                        object posNorm = RequireMember(record.Mod, "posNorm");
                        object rot = RequireMember(record.Mod, "rot");

                        pureOps[i] = new AERIS39MapDecalTangentPureCpuExact.OpSnapshot(
                            ReadDouble(record.Mod, "radius"),
                            ReadDouble(pqs, "radius"),
                            ReadDouble(record.Mod, "heightMapDeformity"),
                            (bool)RequireMember(record.Mod, "cullBlack"),
                            (bool)RequireMember(record.Mod, "useAlphaHeightSmoothing"),
                            (bool)RequireMember(record.Mod, "absolute"),
                            ReadDouble(record.Mod, "absoluteOffset"),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothHeight"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothHR"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothH1M"), CultureInfo.InvariantCulture),
                            (bool)RequireMember(record.Mod, "quadActive"),
                            (bool)RequireMember(record.Mod, "buildHeight"),
                            (bool)RequireMember(pqs, "isBuildingMaps"),
                            ReadDouble(record.Mod, "inclusionAngle"),
                            ReadDouble(posNorm, "x"),
                            ReadDouble(posNorm, "y"),
                            ReadDouble(posNorm, "z"),
                            Convert.ToSingle(RequireMember(rot, "x"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "y"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "z"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "w"), CultureInfo.InvariantCulture),
                            mapSnapshot);
                        break;
                    }

                    case "PQSMod_MapDecal":
                    {
                        MapSO map = ReadMember(record.Mod, "heightMap") as MapSO;
                        AERIS39MapSoPureCpuExact.MapSnapshot mapSnapshot =
                            map == null ? null : SnapshotMap(map);
                        object posNorm = RequireMember(record.Mod, "posNorm");
                        object rot = RequireMember(record.Mod, "rot");

                        pureOps[i] = new AERIS39MapDecalPureCpuExact.OpSnapshot(
                            ReadDouble(record.Mod, "radius"),
                            ReadDouble(pqs, "radius"),
                            ReadDouble(record.Mod, "heightMapDeformity"),
                            (bool)RequireMember(record.Mod, "cullBlack"),
                            (bool)RequireMember(record.Mod, "useAlphaHeightSmoothing"),
                            (bool)RequireMember(record.Mod, "absolute"),
                            ReadDouble(record.Mod, "absoluteOffset"),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothHeight"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothHR"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(record.Mod, "smoothH1M"), CultureInfo.InvariantCulture),
                            (bool)RequireMember(record.Mod, "quadActive"),
                            (bool)RequireMember(record.Mod, "buildHeight"),
                            (bool)RequireMember(pqs, "isBuildingMaps"),
                            ReadDouble(record.Mod, "inclusionAngle"),
                            ReadDouble(posNorm, "x"),
                            ReadDouble(posNorm, "y"),
                            ReadDouble(posNorm, "z"),
                            Convert.ToSingle(RequireMember(rot, "x"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "y"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "z"), CultureInfo.InvariantCulture),
                            Convert.ToSingle(RequireMember(rot, "w"), CultureInfo.InvariantCulture),
                            mapSnapshot);
                        break;
                    }

                    case "PQSMod_FlattenArea":
                    {
                        object posNorm = RequireMember(record.Mod, "posNorm");
                        pureOps[i] = new AERIS39FlattenAreaPureCpuExact.OpSnapshot(
                            (bool)RequireMember(record.Mod, "overrideQuadBuildCheck"),
                            (bool)RequireMember(record.Mod, "quadActive"),
                            ReadDouble(posNorm, "x"),
                            ReadDouble(posNorm, "y"),
                            ReadDouble(posNorm, "z"),
                            ReadDouble(record.Mod, "angleInner"),
                            ReadDouble(record.Mod, "angleOuter"),
                            ReadDouble(record.Mod, "angleQuadInclusion"),
                            ReadDouble(record.Mod, "angleDelta"),
                            ReadDouble(record.Mod, "flattenToRadius"),
                            ReadDouble(record.Mod, "smoothStart"),
                            ReadDouble(record.Mod, "smoothEnd"));
                        break;
                    }

                    case "PQSLandControl":
                        pureOps[i] = SnapshotLandControl(bodyName, record.Mod, pqs);
                        break;

                    case "PQSMod_VertexVoronoi":
                    {
                        object runtimeVoronoi = RequireMember(record.Mod, "voronoi");
                        if (!string.Equals(
                            TypeName(runtimeVoronoi.GetType()),
                            "LibNoise.Voronoi",
                            StringComparison.Ordinal))
                            throw new InvalidOperationException(bodyName + "_VERTEX_VORONOI_RUNTIME_TYPE");

                        double frequency = ReadDouble(record.Mod, "voronoiFrequency");
                        double displacement = ReadDouble(record.Mod, "voronoiDisplacement");
                        int seed = ReadInt(record.Mod, "voronoiSeed");
                        bool distanceEnabled = (bool)RequireMember(record.Mod, "voronoiEnableDistance");
                        double deformation = ReadDouble(record.Mod, "deformation");

                        if (BitConverter.DoubleToInt64Bits(frequency) !=
                                BitConverter.DoubleToInt64Bits(ReadDouble(runtimeVoronoi, "<Frequency>k__BackingField")) ||
                            BitConverter.DoubleToInt64Bits(displacement) !=
                                BitConverter.DoubleToInt64Bits(ReadDouble(runtimeVoronoi, "<Displacement>k__BackingField")) ||
                            seed != ReadInt(runtimeVoronoi, "<Seed>k__BackingField") ||
                            distanceEnabled != (bool)RequireMember(runtimeVoronoi, "<DistanceEnabled>k__BackingField"))
                            throw new InvalidOperationException(bodyName + "_VERTEX_VORONOI_ONSETUP_STATE_MISMATCH");

                        pureOps[i] = new AERIS41VertexVoronoiPureCpuExact.OpSnapshot(
                            frequency,
                            displacement,
                            seed,
                            distanceEnabled,
                            deformation);
                        break;
                    }

                    case "PQSMod_VertexHeightNoiseVertHeight":
                    {
                        object runtimeNoise = RequireMember(record.Mod, "noiseMap");
                        if (!string.Equals(
                            TypeName(runtimeNoise.GetType()),
                            "LibNoise.RidgedMultifractal",
                            StringComparison.Ordinal))
                            throw new InvalidOperationException(bodyName + "_HEIGHTNOISE_RUNTIME_TYPE");

                        object noiseType = RequireMember(record.Mod, "noiseType");
                        if (!string.Equals(noiseType.ToString(), "RidgedMultifractal", StringComparison.Ordinal))
                            throw new InvalidOperationException(bodyName + "_HEIGHTNOISE_NOISE_TYPE");

                        float deformity = Convert.ToSingle(RequireMember(record.Mod, "deformity"), CultureInfo.InvariantCulture);
                        int seed = ReadInt(record.Mod, "seed");
                        float frequency = Convert.ToSingle(RequireMember(record.Mod, "frequency"), CultureInfo.InvariantCulture);
                        float lacunarity = Convert.ToSingle(RequireMember(record.Mod, "lacunarity"), CultureInfo.InvariantCulture);
                        int octaves = ReadInt(record.Mod, "octaves");
                        int mode = Convert.ToInt32(RequireMember(record.Mod, "mode"), CultureInfo.InvariantCulture);
                        float heightStart = Convert.ToSingle(RequireMember(record.Mod, "heightStart"), CultureInfo.InvariantCulture);
                        float heightEnd = Convert.ToSingle(RequireMember(record.Mod, "heightEnd"), CultureInfo.InvariantCulture);
                        double hDeltaR = ReadDouble(record.Mod, "hDeltaR");
                        AERISR039MinmusPureCpuExact.RidgedSnapshot ridged =
                            SnapshotRidged(runtimeNoise, randomVectors);

                        if (BitConverter.DoubleToInt64Bits((double)frequency) != BitConverter.DoubleToInt64Bits(ridged.Frequency) ||
                            BitConverter.DoubleToInt64Bits((double)lacunarity) != BitConverter.DoubleToInt64Bits(ridged.Lacunarity) ||
                            seed != ridged.Seed || octaves != ridged.OctaveCount || mode != ridged.NoiseQuality)
                            throw new InvalidOperationException(bodyName + "_HEIGHTNOISE_ONSETUP_STATE_MISMATCH");

                        pureOps[i] = new AERIS41VertexHeightNoiseVertHeightPureCpuExact.OpSnapshot(
                            deformity,
                            radiusMin,
                            radiusDelta,
                            heightStart,
                            heightEnd,
                            hDeltaR,
                            ridged);
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);
                }
            }

            chain = new AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot(pureOps);
            return true;
        }

        static AERIS39LandControlPureCpuExact.OpSnapshot SnapshotLandControl(
            string bodyName,
            PQSMod mod,
            object pqs)
        {
            bool useHeightMap = (bool)RequireMember(mod, "useHeightMap");
            AERIS39MapSoPureCpuExact.MapSnapshot landHeightMap = null;
            if (useHeightMap)
            {
                MapSO map = RequireMember(mod, "heightMap") as MapSO;
                if (map == null)
                    throw new InvalidOperationException(bodyName + "_LANDCONTROL_HEIGHTMAP_MISSING");
                landHeightMap = SnapshotMap(map);
            }

            Array classes = RequireMember(mod, "landClasses") as Array;
            if (classes == null)
                throw new InvalidOperationException(bodyName + "_LANDCONTROL_CLASSES_MISSING");

            var pureClasses = new AERIS39LandControlPureCpuExact.LandClassSnapshot[classes.Length];
            for (int ci = 0; ci < classes.Length; ci++)
            {
                object lc = classes.GetValue(ci);
                if (lc == null)
                    throw new InvalidOperationException(bodyName + "_LANDCONTROL_NULL_CLASS_" + ci);

                pureClasses[ci] = new AERIS39LandControlPureCpuExact.LandClassSnapshot(
                    SnapshotRange(RequireMember(lc, "altitudeRange")),
                    SnapshotRange(RequireMember(lc, "latitudeRange")),
                    (bool)RequireMember(lc, "latitudeDouble"),
                    SnapshotRange(RequireMember(lc, "latitudeDoubleRange")),
                    SnapshotRange(RequireMember(lc, "longitudeRange")),
                    Convert.ToSingle(RequireMember(lc, "coverageBlend"), CultureInfo.InvariantCulture),
                    SnapshotSimplex(RequireMember(lc, "coverageSimplex")),
                    ReadDouble(lc, "minimumRealHeight"),
                    ReadDouble(lc, "alterRealHeight"));
            }

            return new AERIS39LandControlPureCpuExact.OpSnapshot(
                useHeightMap,
                landHeightMap,
                Convert.ToSingle(RequireMember(mod, "vHeightMax"), CultureInfo.InvariantCulture),
                ReadDouble(pqs, "radius"),
                Convert.ToSingle(RequireMember(mod, "altitudeBlend"), CultureInfo.InvariantCulture),
                Convert.ToSingle(RequireMember(mod, "latitudeBlend"), CultureInfo.InvariantCulture),
                Convert.ToSingle(RequireMember(mod, "longitudeBlend"), CultureInfo.InvariantCulture),
                SnapshotSimplex(RequireMember(mod, "altitudeSimplex")),
                SnapshotSimplex(RequireMember(mod, "latitudeSimplex")),
                SnapshotSimplex(RequireMember(mod, "longitudeSimplex")),
                pureClasses);
        }

        static AERIS39LandControlPureCpuExact.LerpRangeSnapshot SnapshotRange(object range)
        {
            if (range == null)
                throw new ArgumentNullException("range");
            return new AERIS39LandControlPureCpuExact.LerpRangeSnapshot(
                ReadDouble(range, "startStart"),
                ReadDouble(range, "startEnd"),
                ReadDouble(range, "endStart"),
                ReadDouble(range, "endEnd"),
                ReadDouble(range, "startDelta"),
                ReadDouble(range, "endDelta"));
        }

        static AERIS39MapSoPureCpuExact.MapSnapshot SnapshotMap(MapSO map)
        {
            if (map == null) throw new ArgumentNullException("map");
            byte[] data = RequireMember(map, "_data") as byte[];
            if (data == null)
                throw new InvalidOperationException("MAP_DATA_NOT_BYTE_ARRAY");
            int width = ReadInt(map, "_width");
            int height = ReadInt(map, "_height");
            int bpp = ReadInt(map, "_bpp");
            int rowWidth = ReadInt(map, "_rowWidth");
            if (width <= 0 || height <= 0 || bpp <= 0 || rowWidth <= 0)
                throw new InvalidOperationException("MAP_DIMENSIONS_INVALID");

            string semanticsEvidence;
            AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                AERIS39MapSoRuntimeSemanticsResolver.Resolve(map, out semanticsEvidence);
            if (string.IsNullOrEmpty(semanticsEvidence))
                throw new InvalidOperationException("MAP_SEMANTICS_EVIDENCE_EMPTY");

            return new AERIS39MapSoPureCpuExact.MapSnapshot(
                data, width, height, bpp, rowWidth, semantics);
        }

        static CurveSelection SelectCurveSnapshot(string bodyName, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length == 0)
                throw new InvalidOperationException(bodyName + "_CURVE_NO_KEYS");

            AERISR041MohoDresPureCpuExact.CurveKeySnapshot[] pureKeys = SnapshotCurveKeys(keys);
            int bestMode = 0;
            int bestMatches = -1;
            double bestMaxError = double.PositiveInfinity;
            const int tests = 129;

            // Final R041 repair: native-cache-shaped PolynomialFloat wins an exact tie.
            int[] modePreference = { 1, 0, 2, 3 };
            for (int rank = 0; rank < modePreference.Length; rank++)
            {
                int mode = modePreference[rank];
                var candidate = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                    pureKeys,
                    (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)mode,
                    (int)curve.preWrapMode,
                    (int)curve.postWrapMode);

                int matches = 0;
                double maxError = 0.0;
                for (int i = 0; i < tests; i++)
                {
                    float t = i / (float)(tests - 1);
                    float live = curve.Evaluate(t);
                    float pure = AERISR041MohoDresPureCpuExact.EvaluateCurve(candidate, t);
                    if (FloatBits(live) == FloatBits(pure)) matches++;
                    maxError = Math.Max(maxError, Math.Abs((double)live - (double)pure));
                }

                if (matches > bestMatches ||
                    (matches == bestMatches && maxError < bestMaxError))
                {
                    bestMode = mode;
                    bestMatches = matches;
                    bestMaxError = maxError;
                }
            }

            return new CurveSelection
            {
                Snapshot = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                    pureKeys,
                    (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)bestMode,
                    (int)curve.preWrapMode,
                    (int)curve.postWrapMode),
                Exact = bestMatches == tests,
                Matches = bestMatches,
                Tests = tests,
                MaxAbsError = bestMaxError
            };
        }

        static CurveSelection SelectRidgedCurveSnapshot(string bodyName, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length == 0)
                throw new InvalidOperationException(bodyName + "_RIDGED_CURVE_NO_KEYS");

            AERISR041MohoDresPureCpuExact.CurveKeySnapshot[] pureKeys = SnapshotCurveKeys(keys);
            var snapshot = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                pureKeys,
                AERISR041MohoDresPureCpuExact.CurveEvaluationMode.PolynomialFloat,
                (int)curve.preWrapMode,
                (int)curve.postWrapMode);

            int bestMode = 0;
            int bestMatches = -1;
            double bestMaxError = double.PositiveInfinity;
            const int tests = 129;

            for (int mode = 0;
                mode < AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedCurveEvaluationModeCount;
                mode++)
            {
                var curveMode =
                    (AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedCurveEvaluationMode)mode;
                int matches = 0;
                double maxError = 0.0;
                for (int i = 0; i < tests; i++)
                {
                    float t = i / (float)(tests - 1);
                    float live = curve.Evaluate(t);
                    float pure = AERIS39AllBodyHeightModifierChainPureCpuExact.EvaluateRidgedCurve(
                        snapshot, curveMode, t);
                    if (FloatBits(live) == FloatBits(pure)) matches++;
                    maxError = Math.Max(maxError, Math.Abs((double)live - (double)pure));
                }

                if (matches > bestMatches ||
                    (matches == bestMatches && maxError < bestMaxError))
                {
                    bestMode = mode;
                    bestMatches = matches;
                    bestMaxError = maxError;
                }
            }

            return new CurveSelection
            {
                Snapshot = snapshot,
                RidgedMode =
                    (AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedCurveEvaluationMode)bestMode,
                Exact = bestMatches == tests,
                Matches = bestMatches,
                Tests = tests,
                MaxAbsError = bestMaxError
            };
        }

        static AERISR041MohoDresPureCpuExact.CurveKeySnapshot[] SnapshotCurveKeys(Keyframe[] keys)
        {
            var pureKeys = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                pureKeys[i] = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot(
                    keys[i].time,
                    keys[i].value,
                    keys[i].inTangent,
                    keys[i].outTangent,
                    ReadStructIntDefault(keys[i], "weightedMode", 0));
            }
            return pureKeys;
        }

        static AERISR039MinmusPureCpuExact.SimplexSnapshot SnapshotSimplex(object simplex)
        {
            int[] perm = CopyIntArray(RequireMember(simplex, "perm"), 512, "perm");
            int[][] grad3 = CopyJaggedIntArray(RequireMember(simplex, "grad3"), 12, "grad3");
            return new AERISR039MinmusPureCpuExact.SimplexSnapshot(
                perm,
                grad3,
                ReadDouble(simplex, "frequency"),
                ReadDouble(simplex, "octaves"),
                ReadDouble(simplex, "persistence"));
        }

        static AERISR039MinmusPureCpuExact.RidgedSnapshot SnapshotRidged(
            object noise,
            double[] randomVectors)
        {
            double[] spectral = CopyDoubleArray(
                RequireMember(noise, "SpectralWeights"), 30, "SpectralWeights");
            return new AERISR039MinmusPureCpuExact.RidgedSnapshot(
                ReadDouble(noise, "Frequency"),
                ReadInt(noise, "Seed"),
                ReadInt(noise, "NoiseQuality"),
                ReadDouble(noise, "Lacunarity"),
                ReadInt(noise, "OctaveCount"),
                spectral,
                randomVectors);
        }

        static double[] SnapshotLibNoiseRandomVectors()
        {
            Type basis = typeof(CelestialBody).Assembly.GetType("LibNoise.GradientNoiseBasis", false);
            if (basis == null)
                throw new InvalidOperationException("LIBNOISE_BASIS_MISSING");
            FieldInfo field = FindField(
                basis,
                "RandomVectors",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(basis.FullName, "RandomVectors");
            return CopyDoubleArray(field.GetValue(null), 1024, "RandomVectors");
        }

        static List<ModRecord> CollectHeightMods(object pqs)
        {
            IList list = GetModifierList(pqs);
            if (list == null)
                throw new InvalidOperationException("PQS_MODIFIER_LIST_MISSING");

            var result = new List<ModRecord>();
            for (int i = 0; i < list.Count; i++)
            {
                object raw = list[i];
                if (raw == null || !IsEnabled(raw)) continue;
                PQSMod mod = raw as PQSMod;
                if (mod == null) continue;
                Type type = raw.GetType();
                MethodInfo callback = FindHeightCallback(type);
                if (callback == null || callback.DeclaringType == typeof(PQSMod))
                    continue;

                result.Add(new ModRecord
                {
                    Mod = mod,
                    TypeName = TypeName(type),
                    ShortTypeName = type.Name ?? string.Empty,
                    Order = ReadIntDefault(raw, "order", 0),
                    Index = i
                });
            }

            result.Sort(delegate(ModRecord a, ModRecord b)
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            return result;
        }

        static string Topology(List<ModRecord> mods)
        {
            var parts = new string[mods.Count];
            for (int i = 0; i < mods.Count; i++)
            {
                parts[i] = mods[i].Index.ToString(CultureInfo.InvariantCulture) + ":" +
                    mods[i].ShortTypeName + "@" +
                    mods[i].Order.ToString(CultureInfo.InvariantCulture);
            }
            return string.Join(",", parts);
        }

        // R049: raw PQS modifier indices are diagnostic only. Non-height modifiers
        // can be inserted into the runtime PQS list without changing the height callback
        // chain. Exact certification therefore keys on the complete enabled height-chain
        // sequence after the existing (order, raw-index) sort: type + order + relative
        // position. Unknown/extra height callbacks, order changes, or same-order reorders
        // still change this signature and fail closed.
        static string SemanticTopology(List<ModRecord> mods)
        {
            if (mods == null || mods.Count == 0) return string.Empty;
            var parts = new string[mods.Count];
            for (int i = 0; i < mods.Count; i++)
            {
                parts[i] = mods[i].ShortTypeName + "@" +
                    mods[i].Order.ToString(CultureInfo.InvariantCulture);
            }
            return string.Join(",", parts);
        }

        static string SemanticTopologyFromLegacyExpected(string topology)
        {
            if (string.IsNullOrEmpty(topology)) return string.Empty;
            string[] raw = topology.Split(',');
            var parts = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                string item = raw[i] ?? string.Empty;
                int colon = item.IndexOf(':');
                parts[i] = colon >= 0 && colon + 1 < item.Length ?
                    item.Substring(colon + 1) : item;
            }
            return string.Join(",", parts);
        }

        internal static string ExpectedR041Topology(string bodyName)
        {
            if (string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@10,2:PQSMod_VertexRidgedAltitudeCurve@16,3:PQSMod_VertexSimplexHeightAbsolute@20,4:PQSMod_VertexHeightNoiseVertHeightCurve2@32,44:PQSMod_MapDecalTangent@9999,46:PQSMod_MapDecal@99999,47:PQSMod_FlattenArea@99999,48:PQSMod_FlattenArea@99999,49:PQSMod_MapDecalTangent@99999,51:PQSLandControl@9999991";
            if (string.Equals(bodyName, "Eve", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@10,2:PQSMod_VertexSimplexHeight@11,3:PQSMod_VertexHeightNoiseVertHeightCurve2@12,11:PQSLandControl@9999991";
            if (string.Equals(bodyName, "Duna", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@10,2:PQSMod_VertexSimplexHeightAbsolute@12,3:PQSMod_VertexHeightNoiseVertHeightCurve2@13,4:PQSMod_VertexHeightNoiseVertHeightCurve2@14,5:PQSMod_VertexHeightNoiseVertHeightCurve2@15,13:PQSMod_MapDecal@8000,16:PQSLandControl@9999991";
            if (string.Equals(bodyName, "Dres", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@20,2:PQSMod_VertexSimplexHeight@21,3:PQSMod_VertexHeightNoise@22,4:PQSMod_FlattenOcean@25,5:PQSMod_VertexSimplexHeightAbsolute@30";
            if (string.Equals(bodyName, "Moho", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@20,2:PQSMod_VertexSimplexHeight@21,3:PQSMod_FlattenOcean@22,4:PQSMod_VertexHeightNoiseVertHeightCurve2@23,5:PQSMod_VertexSimplexHeightAbsolute@30";
            if (string.Equals(bodyName, "Eeloo", StringComparison.OrdinalIgnoreCase))
                return "1:PQSMod_VertexHeightMap@10,2:PQSMod_VertexSimplexHeight@20,3:PQSMod_FlattenOcean@21,4:PQSMod_VertexHeightNoise@22,5:PQSMod_VertexVoronoi@23,6:PQSMod_VertexHeightNoiseVertHeight@30,13:PQSLandControl@9999991";
            return string.Empty;
        }

        static MethodInfo FindHeightCallback(Type type)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                return type.GetMethod(
                    "OnVertexBuildHeight",
                    flags,
                    null,
                    new Type[] { typeof(PQS.VertexBuildData) },
                    null);
            }
            catch
            {
                return null;
            }
        }

        static IList GetModifierList(object pqs)
        {
            object raw = ReadMember(pqs, "mods") ??
                ReadMember(pqs, "modifiers") ??
                ReadMember(pqs, "pqsMods");
            return raw as IList;
        }

        static bool IsEnabled(object mod)
        {
            object raw = ReadMember(mod, "modEnabled");
            if (raw is bool) return (bool)raw;
            raw = ReadMember(mod, "enabled");
            if (raw is bool) return (bool)raw;
            return true;
        }

        static object RequireMember(object target, string name)
        {
            object value = ReadMember(target, name);
            if (value == null)
                throw new MissingMemberException(
                    target == null ? "NULL" : TypeName(target.GetType()), name);
            return value;
        }

        static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo field = FindField(type, name, flags);
            if (field != null)
            {
                try { return field.GetValue(field.IsStatic ? null : target); }
                catch { }
            }

            PropertyInfo property = FindProperty(type, name);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    MethodInfo getter = property.GetGetMethod(true);
                    return property.GetValue(
                        getter != null && getter.IsStatic ? null : target, null);
                }
                catch { }
            }

            field = FindField(type, "<" + name + ">k__BackingField", flags);
            if (field != null)
            {
                try { return field.GetValue(field.IsStatic ? null : target); }
                catch { }
            }
            return null;
        }

        static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        static double ReadDouble(object target, string name)
        {
            return Convert.ToDouble(RequireMember(target, name), CultureInfo.InvariantCulture);
        }

        static int ReadInt(object target, string name)
        {
            return Convert.ToInt32(RequireMember(target, name), CultureInfo.InvariantCulture);
        }

        static int ReadIntDefault(object target, string name, int fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static int ReadStructIntDefault(object value, string name, int fallback)
        {
            if (value == null) return fallback;
            try
            {
                object raw = ReadMember(value, name);
                return raw == null ? fallback : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        static int[] CopyIntArray(object value, int expected, string name)
        {
            int[] array = value as int[];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            return (int[])array.Clone();
        }

        static int[][] CopyJaggedIntArray(object value, int expected, string name)
        {
            int[][] array = value as int[][];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            int[][] copy = new int[array.Length][];
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].Length < 3)
                    throw new InvalidOperationException(name + "_ROW_" + i);
                copy[i] = (int[])array[i].Clone();
            }
            return copy;
        }

        static double[] CopyDoubleArray(object value, int expected, string name)
        {
            double[] array = value as double[];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            return (double[])array.Clone();
        }

        static int FloatBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        static string BuildEnvironmentHash(
            string certificate,
            string bodyName,
            string topology,
            double pqsRadius,
            double radiusMin)
        {
            string payload =
                "R042_PHASE4B_RUNTIME_CAPTURE_V1|" +
                (certificate ?? string.Empty) + "|" +
                (bodyName ?? string.Empty) + "|" +
                (topology ?? string.Empty) + "|" +
                unchecked((ulong)BitConverter.DoubleToInt64Bits(pqsRadius)).ToString("X16", CultureInfo.InvariantCulture) + "|" +
                unchecked((ulong)BitConverter.DoubleToInt64Bits(radiusMin)).ToString("X16", CultureInfo.InvariantCulture);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var text = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                    text.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        static string TypeName(Type type)
        {
            return type == null ? string.Empty : (type.FullName ?? type.Name ?? string.Empty);
        }
    }
}
