using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // R041 ALL-BODY HEIGHT MODIFIER CHAIN SHADOW.
    //
    // Main thread:
    // - discovers every enabled PQS modifier that actually overrides
    //   OnVertexBuildHeight for the six stock HeightMap-family bodies;
    // - snapshots the supported modifier chain in exact order;
    // - invokes the REAL runtime callback chain only against a newly allocated,
    //   isolated PQS.VertexBuildData witness (never live PQS build state);
    // - reduces all worker payload to primitives/immutable pure snapshots.
    // Worker:
    // - replays the same ordered chain with pure CLR evaluators only;
    // - requires exact IEEE-754 double-bit parity with the real callback chain.
    //
    // No producer switch, DB write, preload mutation, or live PQS state mutation.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39AllBodyHeightModifierChainShadowObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V1";
        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class CoordinateSample
        {
            internal string Label;
            internal double U;
            internal double V;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
        }

        sealed class ExpectedCheck
        {
            internal string Label;
            internal double U;
            internal double V;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal double InputHeight;
            internal bool HasValue;
            internal long ValueBits;
            internal string ExceptionType;
        }

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

        sealed class BodyCase
        {
            internal string Name;
            internal int HeightModifiers;
            internal string Topology;
            internal bool CurveDependenciesExact;
            internal AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot Snapshot;
            internal ExpectedCheck[] Checks;
        }

        sealed class BodyResult
        {
            internal string Name;
            internal int Checks;
            internal int ValueMatches;
            internal int ExceptionMatches;
            internal int ExceptionMismatches;
            internal int Mismatches;
            internal string[] FirstMismatches;
            internal bool Pass;
        }

        sealed class WorkerResult
        {
            internal int WorkerThreadId;
            internal BodyResult[] Bodies;
            internal string Error;
        }

        int mainThreadId;
        bool started;
        bool reported;
        float nextAttempt;
        Task<WorkerResult> workerTask;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;

            if (!started)
            {
                if (Time.realtimeSinceStartup < nextAttempt) return;
                nextAttempt = Time.realtimeSinceStartup + 1f;
                if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
                StartShadow();
                return;
            }

            if (workerTask == null || !workerTask.IsCompleted) return;

            reported = true;
            try
            {
                Report(workerTask.Result);
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHT_CHAIN_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER_RESULT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        void StartShadow()
        {
            started = true;

            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN" +
                "; callback_invocation_thread=MAIN_THREAD_ONLY" +
                "; callback_data=ISOLATED_NEW_VERTEXBUILDDATA" +
                "; live_pqs_state_mutation=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            try
            {
                double[] randomVectors = SnapshotLibNoiseRandomVectors();
                var cases = new BodyCase[TargetBodies.Length];
                for (int i = 0; i < TargetBodies.Length; i++)
                    cases[i] = CaptureBody(TargetBodies[i], randomVectors);

                // HARD BOUNDARY: cases contain no Unity/KSP/runtime objects.
                BodyCase[] purePayload = cases;
                workerTask = Task.Factory.StartNew(
                    () => RunWorker(purePayload),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                reported = true;
                AERISLogger.Error(
                    "[AERIS39][HEIGHT_CHAIN_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=MAIN_THREAD_SNAPSHOT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        BodyCase CaptureBody(string bodyName, double[] randomVectors)
        {
            CelestialBody body = FindBody(bodyName);
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException(bodyName + "_PQS_MISSING");

            object pqs = body.pqsController;
            double radiusMin = ReadDouble(pqs, "radiusMin");
            List<ModRecord> mods = CollectHeightMods(pqs);
            if (mods.Count == 0)
                throw new InvalidOperationException(bodyName + "_NO_ENABLED_HEIGHT_MODIFIERS");

            var pureOps = new AERISR041MohoDresPureCpuExact.HeightOpSnapshot[mods.Count];
            bool curveExact = true;
            var topology = new List<string>(mods.Count);

            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                topology.Add(
                    record.Index.ToString(CultureInfo.InvariantCulture) + ":" +
                    record.ShortTypeName + "@" +
                    record.Order.ToString(CultureInfo.InvariantCulture));

                switch (record.ShortTypeName)
                {
                    case "PQSMod_VertexHeightMap":
                    {
                        MapSO map = RequireMember(record.Mod, "heightMap") as MapSO;
                        if (map == null)
                            throw new InvalidOperationException(bodyName + "_HEIGHTMAP_MAPSO_MISSING");

                        byte[] data = RequireMember(map, "_data") as byte[];
                        if (data == null)
                            throw new InvalidOperationException(bodyName + "_MAP_DATA_NOT_BYTE_ARRAY");

                        int width = ReadInt(map, "_width");
                        int height = ReadInt(map, "_height");
                        int bpp = ReadInt(map, "_bpp");
                        int rowWidth = ReadInt(map, "_rowWidth");
                        if (width <= 0 || height <= 0 || bpp <= 0 || rowWidth <= 0)
                            throw new InvalidOperationException(bodyName + "_MAP_DIMENSIONS_INVALID");

                        string semanticsEvidence;
                        AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                            AERIS39MapSoRuntimeSemanticsResolver.Resolve(map, out semanticsEvidence);

                        var mapSnapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                            data, width, height, bpp, rowWidth, semantics);
                        var heightMap = new AERIS39HeightMapPureCpuExact.Snapshot(
                            ReadDouble(record.Mod, "heightMapOffset"),
                            ReadDouble(record.Mod, "heightMapDeformity"),
                            mapSnapshot);
                        pureOps[i] =
                            new AERIS39AllBodyHeightModifierChainPureCpuExact.CertifiedHeightMapOpSnapshot(
                                heightMap);

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_VertexHeightMap" +
                            "; dependency=MAPSO" +
                            "; map_semantics=" + semantics.ToString() +
                            "; semantics_evidence=" + Safe(semanticsEvidence) +
                            "; exact=true" + Invariants());
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
                        curveExact &= selection.Exact;
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
                        curveExact &= selection.Exact;
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

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_VertexRidgedAltitudeCurve" +
                            "; dependency=SIMPLEX_RIDGED_ANIMATION_CURVE" +
                            "; curve_mode=" + selection.RidgedMode +
                            "; exact=true" + Invariants());
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
                        string runtimeType = TypeName(noise.GetType());
                        if (!string.Equals(runtimeType, "LibNoise.RidgedMultifractal", StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHT_NOISE_NOT_RIDGED:" + runtimeType);

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
                        AERIS39MapSoPureCpuExact.MapSnapshot mapSnapshot = null;
                        string semanticsText = "NONE";
                        string semanticsEvidence = "HEIGHT_MAP_NULL";

                        if (map != null)
                        {
                            byte[] data = RequireMember(map, "_data") as byte[];
                            if (data == null)
                                throw new InvalidOperationException(
                                    bodyName + "_MAPDECAL_MAP_DATA_NOT_BYTE_ARRAY");

                            int width = ReadInt(map, "_width");
                            int height = ReadInt(map, "_height");
                            int bpp = ReadInt(map, "_bpp");
                            int rowWidth = ReadInt(map, "_rowWidth");
                            if (width <= 0 || height <= 0 || bpp <= 0 || rowWidth <= 0)
                                throw new InvalidOperationException(
                                    bodyName + "_MAPDECAL_MAP_DIMENSIONS_INVALID");

                            AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                                AERIS39MapSoRuntimeSemanticsResolver.Resolve(
                                    map, out semanticsEvidence);
                            semanticsText = semantics.ToString();
                            mapSnapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                                data, width, height, bpp, rowWidth, semantics);
                        }

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
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothHeight"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothHR"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothH1M"),
                                CultureInfo.InvariantCulture),
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

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_MapDecalTangent" +
                            "; dependency=MAPSO_HEIGHT_ALPHA_TANGENT" +
                            "; map_semantics=" + Safe(semanticsText) +
                            "; semantics_evidence=" + Safe(semanticsEvidence) +
                            "; runtime_setup_state=SNAPSHOTTED" +
                            "; source_semantics=STOCK_ONVERTEXBUILDHEIGHT" +
                            "; exact_candidate=true" + Invariants());
                        break;
                    }

                    case "PQSMod_MapDecal":
                    {
                        MapSO map = ReadMember(record.Mod, "heightMap") as MapSO;
                        AERIS39MapSoPureCpuExact.MapSnapshot mapSnapshot = null;
                        string semanticsText = "NONE";
                        string semanticsEvidence = "HEIGHT_MAP_NULL";

                        if (map != null)
                        {
                            byte[] data = RequireMember(map, "_data") as byte[];
                            if (data == null)
                                throw new InvalidOperationException(
                                    bodyName + "_MAPDECAL_CLASSIC_MAP_DATA_NOT_BYTE_ARRAY");

                            int width = ReadInt(map, "_width");
                            int height = ReadInt(map, "_height");
                            int bpp = ReadInt(map, "_bpp");
                            int rowWidth = ReadInt(map, "_rowWidth");
                            if (width <= 0 || height <= 0 || bpp <= 0 || rowWidth <= 0)
                                throw new InvalidOperationException(
                                    bodyName + "_MAPDECAL_CLASSIC_MAP_DIMENSIONS_INVALID");

                            AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                                AERIS39MapSoRuntimeSemanticsResolver.Resolve(
                                    map, out semanticsEvidence);
                            semanticsText = semantics.ToString();
                            mapSnapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                                data, width, height, bpp, rowWidth, semantics);
                        }

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
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothHeight"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothHR"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "smoothH1M"),
                                CultureInfo.InvariantCulture),
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

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_MapDecal" +
                            "; dependency=MAPSO_HEIGHT_ALPHA_DECAL" +
                            "; map_semantics=" + Safe(semanticsText) +
                            "; semantics_evidence=" + Safe(semanticsEvidence) +
                            "; runtime_setup_state=SNAPSHOTTED" +
                            "; source_semantics=STOCK_ONVERTEXBUILDHEIGHT" +
                            "; remove_scatter_side_effect=REFERENCE_ONLY" +
                            "; exact_candidate=true" + Invariants());
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);
                }
            }

            var chain = new AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot(pureOps);
            List<CoordinateSample> coords = BuildSamples(bodyName, mods.Count);
            double[] seeds = BuildSeedHeights(body.Radius);
            var checks = new List<ExpectedCheck>(coords.Count * seeds.Length);

            for (int c = 0; c < coords.Count; c++)
            {
                CoordinateSample coord = coords[c];
                for (int h = 0; h < seeds.Length; h++)
                {
                    var check = new ExpectedCheck
                    {
                        Label = coord.Label + " HEIGHT_" + h.ToString(CultureInfo.InvariantCulture),
                        U = coord.U,
                        V = coord.V,
                        Latitude = coord.Latitude,
                        Longitude = coord.Longitude,
                        X = coord.X,
                        Y = coord.Y,
                        Z = coord.Z,
                        InputHeight = seeds[h]
                    };
                    CaptureCallbackChainReference(mods, check);
                    checks.Add(check);
                }
            }

            string topologyText = string.Join(",", topology.ToArray());
            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_SNAPSHOT]" +
                "; candidate=" + Candidate +
                "; body=" + Safe(bodyName) +
                "; height_modifiers=" + mods.Count.ToString(CultureInfo.InvariantCulture) +
                "; topology=" + Safe(topologyText) +
                "; coordinate_samples=" + coords.Count.ToString(CultureInfo.InvariantCulture) +
                "; height_seeds=" + seeds.Length.ToString(CultureInfo.InvariantCulture) +
                "; checks=" + checks.Count.ToString(CultureInfo.InvariantCulture) +
                "; curve_dependencies_exact=" + Bool(curveExact) +
                "; reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN" +
                "; callback_invocation_thread=MAIN_THREAD_ONLY" +
                "; callback_data=ISOLATED_NEW_VERTEXBUILDDATA" +
                "; live_pqs_state_mutation=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            return new BodyCase
            {
                Name = bodyName,
                HeightModifiers = mods.Count,
                Topology = topologyText,
                CurveDependenciesExact = curveExact,
                Snapshot = chain,
                Checks = checks.ToArray()
            };
        }

        List<ModRecord> CollectHeightMods(object pqs)
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
                    ShortTypeName = ShortTypeName(type),
                    Order = ReadIntDefault(raw, "order", 0),
                    Index = i
                });
            }

            result.Sort(delegate(ModRecord a, ModRecord b)
            {
                int c = a.Order.CompareTo(b.Order);
                if (c != 0) return c;
                return a.Index.CompareTo(b.Index);
            });
            return result;
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

        static void CaptureCallbackChainReference(
            List<ModRecord> mods,
            ExpectedCheck check)
        {
            try
            {
                var direction = new Vector3d(check.X, check.Y, check.Z);
                var data = new PQS.VertexBuildData
                {
                    u = check.U,
                    v = check.V,
                    latitude = check.Latitude,
                    longitude = check.Longitude,
                    vertHeight = check.InputHeight,
                    directionFromCenter = direction,
                    directionD = direction,
                    directionXZ = new Vector3d(check.X, 0.0, check.Z),
                    globalV = direction
                };

                for (int i = 0; i < mods.Count; i++)
                    mods[i].Mod.OnVertexBuildHeight(data);

                check.HasValue = true;
                check.ValueBits = BitConverter.DoubleToInt64Bits(data.vertHeight);
                check.ExceptionType = string.Empty;
            }
            catch (Exception ex)
            {
                check.HasValue = false;
                check.ValueBits = 0L;
                check.ExceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            }
        }

        static WorkerResult RunWorker(BodyCase[] cases)
        {
            var result = new WorkerResult
            {
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId
            };

            try
            {
                result.Bodies = new BodyResult[cases.Length];
                for (int i = 0; i < cases.Length; i++)
                    result.Bodies[i] = EvaluateBody(cases[i]);
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().FullName + ":" + ex.Message;
            }
            return result;
        }

        static BodyResult EvaluateBody(BodyCase body)
        {
            var result = new BodyResult { Name = body.Name };
            var mismatches = new List<string>();

            for (int i = 0; i < body.Checks.Length; i++)
            {
                ExpectedCheck expected = body.Checks[i];
                result.Checks++;
                try
                {
                    double pure = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                        body.Snapshot,
                        expected.X,
                        expected.Y,
                        expected.Z,
                        expected.U,
                        expected.V,
                        expected.InputHeight);
                    long bits = AERIS39AllBodyHeightModifierChainPureCpuExact.DoubleBits(pure);

                    if (expected.HasValue && bits == expected.ValueBits)
                    {
                        result.ValueMatches++;
                        continue;
                    }

                    result.Mismatches++;
                    if (!expected.HasValue) result.ExceptionMismatches++;
                    if (mismatches.Count < 12)
                    {
                        mismatches.Add(
                            expected.Label +
                            " native=" + (expected.HasValue
                                ? "0x" + unchecked((ulong)expected.ValueBits).ToString("X16", CultureInfo.InvariantCulture)
                                : "EX:" + expected.ExceptionType) +
                            " pure=0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception ex)
                {
                    string type = ex.GetType().FullName ?? ex.GetType().Name;
                    if (!expected.HasValue && string.Equals(type, expected.ExceptionType, StringComparison.Ordinal))
                    {
                        result.ExceptionMatches++;
                        continue;
                    }

                    result.Mismatches++;
                    result.ExceptionMismatches++;
                    if (mismatches.Count < 12)
                    {
                        mismatches.Add(
                            expected.Label +
                            " native=" + (expected.HasValue
                                ? "0x" + unchecked((ulong)expected.ValueBits).ToString("X16", CultureInfo.InvariantCulture)
                                : "EX:" + expected.ExceptionType) +
                            " pure=EX:" + type);
                    }
                }
            }

            result.FirstMismatches = mismatches.ToArray();
            result.Pass =
                body.CurveDependenciesExact &&
                result.Checks > 0 &&
                result.Mismatches == 0 &&
                result.ExceptionMismatches == 0;
            return result;
        }

        void Report(WorkerResult result)
        {
            if (result == null)
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHT_CHAIN_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=NULL_WORKER_RESULT" + Invariants());
                return;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHT_CHAIN_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER" +
                    "; error=" + Safe(result.Error) + Invariants());
                return;
            }

            bool workerNotMain = result.WorkerThreadId != mainThreadId;
            bool pass = workerNotMain && result.Bodies != null && result.Bodies.Length == TargetBodies.Length;
            int checks = 0;
            int matches = 0;
            int exceptionMatches = 0;
            int exceptionMismatches = 0;
            int mismatches = 0;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                pass &= body != null && body.Pass;
                if (body == null) continue;

                checks += body.Checks;
                matches += body.ValueMatches;
                exceptionMatches += body.ExceptionMatches;
                exceptionMismatches += body.ExceptionMismatches;
                mismatches += body.Mismatches;

                AERISLogger.Info(
                    "[AERIS39][HEIGHT_CHAIN_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(body.Name) +
                    "; pass=" + Bool(body.Pass) +
                    "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                    "; value_matches=" + body.ValueMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_matches=" + body.ExceptionMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_mismatches=" + body.ExceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                    "; mismatch_count=" + body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                    "; bit_exact=" + Bool(body.Mismatches == 0) +
                    "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN" +
                    "; callback_invocation_thread=MAIN_THREAD_ONLY" +
                    "; callback_data=ISOLATED_NEW_VERTEXBUILDDATA" +
                    "; live_pqs_state_mutation=false" +
                    "; snapshot_payload=PRIMITIVES_ONLY" +
                    Invariants());

                if (body.FirstMismatches == null) continue;
                for (int m = 0; m < body.FirstMismatches.Length; m++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][HEIGHT_CHAIN_MISMATCH]" +
                        "; body=" + Safe(body.Name) +
                        "; detail=" + Safe(body.FirstMismatches[m]) + Invariants());
                }
            }

            pass &= checks > 0 && mismatches == 0 && exceptionMismatches == 0;

            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; candidate=" + Candidate +
                "; bodies=" + (result.Bodies == null ? 0 : result.Bodies.Length).ToString(CultureInfo.InvariantCulture) +
                "; total_checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                "; value_matches=" + matches.ToString(CultureInfo.InvariantCulture) +
                "; exception_match_count=" + exceptionMatches.ToString(CultureInfo.InvariantCulture) +
                "; exception_mismatch_count=" + exceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(mismatches == 0) +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN" +
                "; callback_invocation_thread=MAIN_THREAD_ONLY" +
                "; callback_data=ISOLATED_NEW_VERTEXBUILDDATA" +
                "; live_pqs_state_mutation=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());
        }

        CurveSelection SelectCurveSnapshot(string bodyName, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length == 0)
                throw new InvalidOperationException(bodyName + "_CURVE_NO_KEYS");

            var pureKeys = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                int weightedMode = ReadStructIntDefault(keys[i], "weightedMode", 0);
                pureKeys[i] = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot(
                    keys[i].time,
                    keys[i].value,
                    keys[i].inTangent,
                    keys[i].outTangent,
                    weightedMode);
            }

            int bestMode = 0;
            int bestMatches = -1;
            double bestMaxError = double.PositiveInfinity;
            const int tests = 129;

            for (int mode = 0; mode < 4; mode++)
            {
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

            var selected = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                pureKeys,
                (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)bestMode,
                (int)curve.preWrapMode,
                (int)curve.postWrapMode);
            bool exact = bestMatches == tests;

            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                "; body=" + Safe(bodyName) +
                "; dependency=ANIMATION_CURVE" +
                "; evaluation_mode=" + selected.Mode +
                "; matches=" + bestMatches.ToString(CultureInfo.InvariantCulture) +
                "; tests=" + tests.ToString(CultureInfo.InvariantCulture) +
                "; max_abs_error=" + R(bestMaxError) +
                "; exact=" + Bool(exact) +
                "; live_calls_thread=MAIN_THREAD_ONLY" + Invariants());

            return new CurveSelection
            {
                Snapshot = selected,
                Exact = exact,
                Matches = bestMatches,
                Tests = tests,
                MaxAbsError = bestMaxError
            };
        }

        CurveSelection SelectRidgedCurveSnapshot(string bodyName, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length == 0)
                throw new InvalidOperationException(bodyName + "_RIDGED_CURVE_NO_KEYS");

            var pureKeys = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                int weightedMode = ReadStructIntDefault(keys[i], "weightedMode", 0);
                pureKeys[i] = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot(
                    keys[i].time,
                    keys[i].value,
                    keys[i].inTangent,
                    keys[i].outTangent,
                    weightedMode);
            }

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

                AERISLogger.Info(
                    "[AERIS39][HEIGHT_CHAIN_CURVE_CANDIDATE]" +
                    "; body=" + Safe(bodyName) +
                    "; type=PQSMod_VertexRidgedAltitudeCurve" +
                    "; evaluation_mode=" + curveMode +
                    "; matches=" + matches.ToString(CultureInfo.InvariantCulture) +
                    "; tests=" + tests.ToString(CultureInfo.InvariantCulture) +
                    "; max_abs_error=" + R(maxError) +
                    "; exact=" + Bool(matches == tests) +
                    "; live_calls_thread=MAIN_THREAD_ONLY" + Invariants());

                if (matches > bestMatches ||
                    (matches == bestMatches && maxError < bestMaxError))
                {
                    bestMode = mode;
                    bestMatches = matches;
                    bestMaxError = maxError;
                }
            }

            var selectedMode =
                (AERIS39AllBodyHeightModifierChainPureCpuExact.RidgedCurveEvaluationMode)bestMode;
            bool exact = bestMatches == tests;

            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                "; body=" + Safe(bodyName) +
                "; dependency=ANIMATION_CURVE_RIDGED_EXACT" +
                "; evaluation_mode=" + selectedMode +
                "; matches=" + bestMatches.ToString(CultureInfo.InvariantCulture) +
                "; tests=" + tests.ToString(CultureInfo.InvariantCulture) +
                "; max_abs_error=" + R(bestMaxError) +
                "; weighted_keys=" + Bool(snapshot.HasWeightedKeys) +
                "; exact=" + Bool(exact) +
                "; live_calls_thread=MAIN_THREAD_ONLY" + Invariants());

            return new CurveSelection
            {
                Snapshot = snapshot,
                RidgedMode = selectedMode,
                Exact = exact,
                Matches = bestMatches,
                Tests = tests,
                MaxAbsError = bestMaxError
            };
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

        static double[] BuildSeedHeights(double radius)
        {
            return new[]
            {
                0.0,
                radius,
                radius + 0.125,
                radius - 1234.56789,
                -987.654321
            };
        }

        static List<CoordinateSample> BuildSamples(string bodyName, int modifierCount)
        {
            var result = new List<CoordinateSample>(700);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            double[] latitudes =
            {
                -90.0, -75.0, -60.0, -45.0, -30.0, -15.0,
                0.0, 15.0, 30.0, 45.0, 60.0, 75.0, 90.0
            };
            double[] longitudes =
            {
                -180.0, -135.0, -90.0, -45.0, 0.0,
                45.0, 90.0, 135.0, 180.0
            };

            for (int a = 0; a < latitudes.Length; a++)
            {
                for (int o = 0; o < longitudes.Length; o++)
                {
                    AddSample(
                        result,
                        seen,
                        "BODY_COORD lat=" + R(latitudes[a]) + " lon=" + R(longitudes[o]),
                        longitudes[o] / 360.0 + 0.5,
                        latitudes[a] / 180.0 + 0.5);
                }
            }

            const int nominalWidth = 4096;
            const int nominalHeight = 2048;
            int[] xs = DistinctIndices(nominalWidth);
            int[] ys = DistinctIndices(nominalHeight);
            for (int i = 0; i < xs.Length; i++)
            {
                double u = (double)xs[i] / nominalWidth;
                AddBoundarySamples(result, seen, "X_EDGE_" + xs[i], u, 0.37109375, true);
            }
            for (int i = 0; i < ys.Length; i++)
            {
                double v = (double)ys[i] / nominalHeight;
                AddBoundarySamples(result, seen, "Y_EDGE_" + ys[i], 0.62890625, v, false);
            }

            uint state = Seed(bodyName, modifierCount);
            for (int i = 0; i < 384; i++)
            {
                state = Next(state);
                double u = (double)state / 4294967296.0;
                state = Next(state);
                double v = (double)state / 4294967296.0;
                AddSample(result, seen, "RANDOM_" + i.ToString(CultureInfo.InvariantCulture), u, v);
            }

            AddSample(result, seen, "PERIODIC_NEG_U", -0.125, 0.375);
            AddSample(result, seen, "PERIODIC_POS_U", 1.125, 0.375);
            AddSample(result, seen, "PERIODIC_NEG_V", 0.625, -0.125);
            AddSample(result, seen, "PERIODIC_POS_V", 0.625, 1.125);

            if (result.Count != 565)
                throw new InvalidOperationException(
                    bodyName + "_EXPECTED_565_COORDINATES_ACTUAL_" +
                    result.Count.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        static void AddSample(
            List<CoordinateSample> result,
            HashSet<string> seen,
            string label,
            double u,
            double v)
        {
            string key = BitConverter.DoubleToInt64Bits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;

            double latitude = (v - 0.5) * 180.0;
            double longitude = (u - 0.5) * 360.0;
            double latRad = latitude * (Math.PI / 180.0);
            double lonRad = longitude * (Math.PI / 180.0);
            double cosLat = Math.Cos(latRad);

            result.Add(new CoordinateSample
            {
                Label = label,
                U = u,
                V = v,
                Latitude = latitude,
                Longitude = longitude,
                X = cosLat * Math.Cos(lonRad),
                Y = Math.Sin(latRad),
                Z = cosLat * Math.Sin(lonRad)
            });
        }

        static int[] DistinctIndices(int dimension)
        {
            var values = new List<int>();
            AddIndex(values, 0, dimension);
            AddIndex(values, 1, dimension);
            AddIndex(values, dimension / 4, dimension);
            AddIndex(values, dimension / 3, dimension);
            AddIndex(values, dimension / 2, dimension);
            AddIndex(values, dimension * 2 / 3, dimension);
            AddIndex(values, dimension * 3 / 4, dimension);
            AddIndex(values, Math.Max(0, dimension - 2), dimension);
            AddIndex(values, Math.Max(0, dimension - 1), dimension);
            AddIndex(values, dimension, dimension);
            return values.ToArray();
        }

        static void AddIndex(List<int> values, int value, int dimension)
        {
            if (value < 0 || value > dimension) return;
            if (!values.Contains(value)) values.Add(value);
        }

        static void AddBoundarySamples(
            List<CoordinateSample> result,
            HashSet<string> seen,
            string label,
            double axisValue,
            double otherValue,
            bool xAxis)
        {
            double down = NextDoubleDown(axisValue);
            double up = NextDoubleUp(axisValue);
            if (xAxis)
            {
                AddSample(result, seen, label + "_EXACT", axisValue, otherValue);
                AddSample(result, seen, label + "_DOWN", down, otherValue);
                AddSample(result, seen, label + "_UP", up, otherValue);
            }
            else
            {
                AddSample(result, seen, label + "_EXACT", otherValue, axisValue);
                AddSample(result, seen, label + "_DOWN", otherValue, down);
                AddSample(result, seen, label + "_UP", otherValue, up);
            }
        }

        static IList GetModifierList(object pqs)
        {
            object raw = ReadMember(pqs, "mods") ?? ReadMember(pqs, "modifiers") ?? ReadMember(pqs, "pqsMods");
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

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null) continue;
                if (string.Equals(body.name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
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
                    throw new InvalidOperationException(name + "_ROW_" + i.ToString(CultureInfo.InvariantCulture));
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

        static uint Seed(string bodyName, int modifierCount)
        {
            uint h = 2166136261u;
            string value = bodyName ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                h ^= value[i];
                h = unchecked(h * 16777619u);
            }
            h ^= unchecked((uint)modifierCount * 0x9E3779B9u);
            return h == 0 ? 1u : h;
        }

        static uint Next(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        static double NextDoubleUp(double value)
        {
            if (double.IsNaN(value) || value == double.PositiveInfinity) return value;
            if (value == 0.0) return BitConverter.Int64BitsToDouble(1L);
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? 1L : -1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        static double NextDoubleDown(double value)
        {
            if (double.IsNaN(value) || value == double.NegativeInfinity) return value;
            if (value == 0.0)
                return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000001UL));
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? -1L : 1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        static int FloatBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        static string TypeName(Type type)
        {
            return type == null ? string.Empty : (type.FullName ?? type.Name ?? string.Empty);
        }

        static string ShortTypeName(Type type)
        {
            return type == null ? string.Empty : (type.Name ?? string.Empty);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }

        static string Invariants()
        {
            return
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; production_worker_runtime_object_access=false";
        }
    }
}
