using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS38 R041B: read-only height-formula family census.
    //
    // R041A intentionally classified the whole enabled PQS stack conservatively.
    // R041B narrows authority to the KSP terrain-height callback itself:
    // PQSMod.OnVertexBuildHeight(PQS.VertexBuildData).
    //
    // Safety contract:
    // - Main-thread observer only.
    // - Reflection metadata/read-only configuration inspection only.
    // - Never invokes PQS modifier callbacks.
    // - Never changes PQS, preload, DB, producer routing, AP/AA/PROTECT/LAND.
    // - Never passes Unity/KSP runtime objects to workers.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR041BHeightFormulaFamilyCensusObserver : MonoBehaviour
    {
        const string CandidateMarker =
            "AERIS38_R041B_HEIGHT_FORMULA_FAMILY_CENSUS";
        const double ExactToleranceMeters = 1E-08;
        const string ProductionAuthority = "PQS";
        const string DbAuthority = "PQS";
        const string ProducerSwitch = "false";

        bool emitted;
        int mainThreadId;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (emitted) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            TryEmit();
        }

        void TryEmit()
        {
            if (emitted) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            emitted = true;

            try
            {
                int bodies = 0;
                int pqsBodies = 0;
                int noPqsBodies = 0;
                int pureCpuReadyBodies = 0;
                int formulaClosureBodies = 0;
                int stateClosureBodies = 0;
                int totalEnabledModifiers = 0;
                int totalHeightModifiers = 0;
                int totalStatefulHeightModifiers = 0;
                var heightTypes = new HashSet<string>(StringComparer.Ordinal);
                var heightFamilies = new HashSet<string>(StringComparer.Ordinal);

                AERISLogger.Info(
                    "[R041B][HEIGHT_CENSUS_BEGIN]" +
                    "; candidate=" + CandidateMarker +
                    "; main_thread_id=" + mainThreadId +
                    "; height_authority=OnVertexBuildHeight_override_metadata" +
                    "; invokes_pqs_callbacks=false" +
                    "; terrain_tolerance_m=" + R(ExactToleranceMeters) +
                    "; production_authority=" + ProductionAuthority +
                    "; db_authority=" + DbAuthority +
                    "; producer_switch=" + ProducerSwitch +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false");

                for (int bodyIndex = 0; bodyIndex < FlightGlobals.Bodies.Count; bodyIndex++)
                {
                    CelestialBody body = FlightGlobals.Bodies[bodyIndex];
                    if (body == null) continue;
                    bodies++;

                    object pqs = null;
                    try { pqs = body.pqsController; }
                    catch { pqs = null; }

                    if (pqs == null)
                    {
                        noPqsBodies++;
                        AERISLogger.Info(
                            "[R041B][HEIGHT_BODY]" +
                            "; body=" + Sanitize(body.name) +
                            "; pqs_type=-" +
                            "; enabled_modifiers=0" +
                            "; height_modifiers=0" +
                            "; stateful_height_modifiers=0" +
                            "; pure_cpu_accepted=0" +
                            "; non_height_modifiers=0" +
                            "; height_capability=NO_PQS" +
                            "; production_authority=" + ProductionAuthority +
                            "; producer_switch=" + ProducerSwitch);
                        continue;
                    }

                    pqsBodies++;
                    IList modifiers = GetModifierList(pqs);
                    if (modifiers == null)
                    {
                        stateClosureBodies++;
                        AERISLogger.Warn(
                            "[R041B][HEIGHT_BODY]" +
                            "; body=" + Sanitize(body.name) +
                            "; pqs_type=" + Sanitize(pqs.GetType().FullName ?? pqs.GetType().Name) +
                            "; enabled_modifiers=0" +
                            "; height_modifiers=0" +
                            "; stateful_height_modifiers=0" +
                            "; pure_cpu_accepted=0" +
                            "; non_height_modifiers=0" +
                            "; height_capability=MODIFIER_LIST_UNAVAILABLE" +
                            "; production_authority=" + ProductionAuthority +
                            "; producer_switch=" + ProducerSwitch);
                        continue;
                    }

                    int enabledModifiers = 0;
                    int heightModifiers = 0;
                    int statefulHeightModifiers = 0;
                    int pureCpuAccepted = 0;
                    int nonHeightModifiers = 0;

                    for (int i = 0; i < modifiers.Count; i++)
                    {
                        object modifier = modifiers[i];
                        if (modifier == null) continue;

                        bool enabled;
                        if (TryReadBool(modifier, "modEnabled", out enabled) ||
                            TryReadBool(modifier, "enabled", out enabled))
                        {
                            if (!enabled) continue;
                        }

                        enabledModifiers++;
                        totalEnabledModifiers++;

                        Type type = modifier.GetType();
                        string typeName = type.FullName ?? type.Name ?? "UNKNOWN";
                        string assembly = type.Assembly != null
                            ? type.Assembly.GetName().Name ?? string.Empty
                            : string.Empty;

                        string heightDeclaredBy;
                        bool heightOverride = OverridesPqsModCallback(
                            type,
                            "OnVertexBuildHeight",
                            1,
                            out heightDeclaredBy);

                        if (!heightOverride)
                        {
                            nonHeightModifiers++;
                            continue;
                        }

                        heightModifiers++;
                        totalHeightModifiers++;
                        heightTypes.Add(typeName);

                        string family = FormulaFamily(typeName);
                        heightFamilies.Add(family);

                        string quadPreBuildDeclaredBy;
                        bool quadPreBuildOverride = OverridesPqsModCallback(
                            type,
                            "OnQuadPreBuild",
                            1,
                            out quadPreBuildDeclaredBy);

                        string setupDeclaredBy;
                        bool setupOverride = OverridesPqsModCallback(
                            type,
                            "OnSetup",
                            0,
                            out setupDeclaredBy);

                        if (quadPreBuildOverride)
                        {
                            statefulHeightModifiers++;
                            totalStatefulHeightModifiers++;
                        }

                        bool accepted = IsAcceptedPureCpuHeight(body.name, typeName);
                        if (accepted) pureCpuAccepted++;

                        int order;
                        bool hasOrder = TryReadInt(modifier, "order", out order);

                        AERISLogger.Info(
                            "[R041B][HEIGHT_MODIFIER]" +
                            "; body=" + Sanitize(body.name) +
                            "; index=" + i +
                            "; type=" + Sanitize(typeName) +
                            "; assembly=" + Sanitize(assembly) +
                            "; order=" + (hasOrder ? order.ToString(CultureInfo.InvariantCulture) : "-") +
                            "; family=" + family +
                            "; height_callback_declared_by=" + Sanitize(heightDeclaredBy) +
                            "; quad_prebuild=" + Bool(quadPreBuildOverride) +
                            "; quad_prebuild_declared_by=" + Sanitize(quadPreBuildDeclaredBy) +
                            "; setup_override=" + Bool(setupOverride) +
                            "; setup_declared_by=" + Sanitize(setupDeclaredBy) +
                            "; pure_cpu_accepted=" + Bool(accepted) +
                            "; evaluator_state=" +
                                (accepted ? "ACCEPTED" :
                                 quadPreBuildOverride ? "STATE_CLOSURE_REQUIRED" :
                                 "FORMULA_CLOSURE_REQUIRED"));
                    }

                    string bodyCapability;
                    if (heightModifiers > 0 && pureCpuAccepted == heightModifiers)
                    {
                        bodyCapability = "PURE_CPU_READY";
                        pureCpuReadyBodies++;
                    }
                    else if (statefulHeightModifiers > 0)
                    {
                        bodyCapability = "STATE_CLOSURE_REQUIRED";
                        stateClosureBodies++;
                    }
                    else
                    {
                        bodyCapability = "FORMULA_CLOSURE_REQUIRED";
                        formulaClosureBodies++;
                    }

                    AERISLogger.Info(
                        "[R041B][HEIGHT_BODY]" +
                        "; body=" + Sanitize(body.name) +
                        "; pqs_type=" + Sanitize(pqs.GetType().FullName ?? pqs.GetType().Name) +
                        "; enabled_modifiers=" + enabledModifiers +
                        "; height_modifiers=" + heightModifiers +
                        "; stateful_height_modifiers=" + statefulHeightModifiers +
                        "; pure_cpu_accepted=" + pureCpuAccepted +
                        "; non_height_modifiers=" + nonHeightModifiers +
                        "; height_capability=" + bodyCapability +
                        "; production_authority=" + ProductionAuthority +
                        "; producer_switch=" + ProducerSwitch);
                }

                AERISLogger.Info(
                    "[R041B][HEIGHT_CENSUS_COMPLETE]" +
                    "; pass=true" +
                    "; candidate=" + CandidateMarker +
                    "; bodies=" + bodies +
                    "; pqs_bodies=" + pqsBodies +
                    "; no_pqs=" + noPqsBodies +
                    "; enabled_modifiers=" + totalEnabledModifiers +
                    "; height_modifiers=" + totalHeightModifiers +
                    "; height_types=" + heightTypes.Count +
                    "; height_families=" + heightFamilies.Count +
                    "; stateful_height_modifiers=" + totalStatefulHeightModifiers +
                    "; pure_cpu_ready_bodies=" + pureCpuReadyBodies +
                    "; formula_closure_bodies=" + formulaClosureBodies +
                    "; state_closure_bodies=" + stateClosureBodies +
                    "; height_authority=OnVertexBuildHeight_override_metadata" +
                    "; invokes_pqs_callbacks=false" +
                    "; terrain_tolerance_m=" + R(ExactToleranceMeters) +
                    "; production_authority=" + ProductionAuthority +
                    "; db_authority=" + DbAuthority +
                    "; producer_switch=" + ProducerSwitch +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false");
            }
            catch (Exception ex)
            {
                AERISLogger.Warn(
                    "[R041B][HEIGHT_CENSUS_FAIL]" +
                    "; candidate=" + CandidateMarker +
                    "; error=" + Sanitize(ex.GetType().Name + ":" + ex.Message) +
                    "; invokes_pqs_callbacks=false" +
                    "; production_authority=" + ProductionAuthority +
                    "; db_authority=" + DbAuthority +
                    "; producer_switch=" + ProducerSwitch +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false");
            }
        }

        static bool IsAcceptedPureCpuHeight(string bodyName, string typeName)
        {
            return string.Equals(bodyName, "Minmus", StringComparison.OrdinalIgnoreCase) &&
                (typeName ?? string.Empty).EndsWith(
                    "PQSMod_VertexPlanet",
                    StringComparison.Ordinal);
        }

        static string FormulaFamily(string typeName)
        {
            string name = typeName ?? string.Empty;
            if (name.IndexOf("VertexPlanet", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VERTEX_PLANET";
            if (name.IndexOf("VertexSimplexHeightMap", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SIMPLEX_HEIGHT_MAP";
            if (name.IndexOf("VertexHeightMap", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HEIGHT_MAP";
            if (name.IndexOf("VoronoiCraters", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VORONOI_CRATERS";
            if (name.IndexOf("VertexVoronoi", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VERTEX_VORONOI";
            if (name.IndexOf("VertexRidged", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RIDGED_ALTITUDE";
            if (name.IndexOf("VertexHeightNoiseVertHeightCurve2", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HEIGHT_NOISE_VERT_HEIGHT_CURVE2";
            if (name.IndexOf("VertexHeightNoiseVertHeight", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HEIGHT_NOISE_VERT_HEIGHT";
            if (name.IndexOf("VertexHeightNoise", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HEIGHT_NOISE";
            if (name.IndexOf("VertexSimplexHeightAbsolute", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SIMPLEX_HEIGHT_ABSOLUTE";
            if (name.IndexOf("VertexSimplexHeight", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SIMPLEX_HEIGHT";
            if (name.IndexOf("Flatten", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FLATTEN";
            if (name.IndexOf("MapDecal", StringComparison.OrdinalIgnoreCase) >= 0)
                return "MAP_DECAL";
            if (name.IndexOf("VertexHeightOffset", StringComparison.OrdinalIgnoreCase) >= 0)
                return "HEIGHT_OFFSET";
            return "OTHER_HEIGHT";
        }

        static bool OverridesPqsModCallback(
            Type type,
            string methodName,
            int parameterCount,
            out string declaredBy)
        {
            declaredBy = "-";
            if (type == null || string.IsNullOrEmpty(methodName)) return false;

            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo[] methods;
            try { methods = type.GetMethods(flags); }
            catch { return false; }

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;
                if (method.GetParameters().Length != parameterCount)
                    continue;

                Type declaring = method.DeclaringType;
                declaredBy = declaring != null
                    ? (declaring.FullName ?? declaring.Name ?? "UNKNOWN")
                    : "UNKNOWN";
                return declaring != null && declaring != typeof(PQSMod);
            }

            return false;
        }

        static IList GetModifierList(object pqs)
        {
            if (pqs == null) return null;

            Type type = pqs.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            string[] names = { "mods", "modifiers", "pqsMods" };

            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = type.GetField(names[i], flags);
                if (field != null)
                {
                    try
                    {
                        IList list = field.GetValue(pqs) as IList;
                        if (list != null) return list;
                    }
                    catch { }
                }

                PropertyInfo property = type.GetProperty(names[i], flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        IList list = property.GetValue(pqs, null) as IList;
                        if (list != null) return list;
                    }
                    catch { }
                }
            }

            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!typeof(IList).IsAssignableFrom(field.FieldType)) continue;
                try
                {
                    IList list = field.GetValue(pqs) as IList;
                    if (LooksLikeModifierList(list)) return list;
                }
                catch { }
            }

            return null;
        }

        static bool LooksLikeModifierList(IList list)
        {
            if (list == null) return false;
            int inspected = Math.Min(list.Count, 8);
            for (int i = 0; i < inspected; i++)
            {
                object value = list[i];
                if (value == null) continue;
                string name = value.GetType().Name ?? string.Empty;
                if (name.IndexOf("PQSMod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("PQSCity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("PQSLandControl", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return list.Count == 0;
        }

        static bool TryReadBool(object target, string name, out bool value)
        {
            value = false;
            object raw;
            if (!TryReadMember(target, name, out raw) || !(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        static bool TryReadInt(object target, string name, out int value)
        {
            value = 0;
            object raw;
            if (!TryReadMember(target, name, out raw) || raw == null) return false;
            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        static bool TryReadMember(object target, string name, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrEmpty(name)) return false;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try { value = field.GetValue(target); return true; }
                catch { }
            }

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try { value = property.GetValue(target, null); return true; }
                catch { }
            }

            return false;
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
