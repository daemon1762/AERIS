using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS38 R041A: read-only all-body PQS capability/dependency census.
    //
    // Safety contract:
    // - Main-thread observer only.
    // - Never changes PQS, modifiers, preload authority, DB authority, or producer routing.
    // - Never passes Unity/KSP runtime objects to workers.
    // - Capability labels are census results only; producer_switch remains false.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR041AllBodyPqsCapabilityCensusObserver : MonoBehaviour
    {
        enum Capability
        {
            NoPqs,
            Snapshottable,
            PureCpuReady,
            UnsupportedFormula,
            RuntimeDependent
        }

        sealed class ModifierRecord
        {
            internal int Index;
            internal string TypeName = string.Empty;
            internal string AssemblyName = string.Empty;
            internal bool Enabled;
            internal bool HasEnabled;
            internal int Order;
            internal bool HasOrder;
            internal Capability Capability;
            internal string Reason = string.Empty;
        }

        const string CandidateMarker =
            "AERIS38_R041A_ALL_BODY_PQS_CAPABILITY_DEPENDENCY_CENSUS";
        const double ExactToleranceMeters = 1E-08;
        const string ProducerSwitch = "false";
        const string DbAuthority = "PQS";
        const string ProductionAuthority = "PQS";

        static readonly string[] KnownSnapshotAssetFragments =
        {
            "VertexHeightMap",
            "VertexColorMap",
            "LandControl"
        };

        static readonly string[] KnownRuntimeDependentFragments =
        {
            "City",
            "MapDecal",
            "FlattenArea",
            "QuadEnhance"
        };

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
                int noPqs = 0;
                int pureCpuReady = 0;
                int snapshottable = 0;
                int unsupported = 0;
                int runtimeDependent = 0;
                int totalModifiers = 0;

                AERISLogger.Info(
                    "[R041A][ALL_BODY_PQS_CENSUS_BEGIN]" +
                    "; candidate=" + CandidateMarker +
                    "; main_thread_id=" + mainThreadId +
                    "; terrain_tolerance_m=" + R(ExactToleranceMeters) +
                    "; production_authority=" + ProductionAuthority +
                    "; db_authority=" + DbAuthority +
                    "; producer_switch=" + ProducerSwitch +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false" +
                    "; mode=READ_ONLY_CAPABILITY_CENSUS");

                for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
                {
                    CelestialBody body = FlightGlobals.Bodies[i];
                    if (body == null) continue;

                    bodies++;
                    Capability bodyCapability;
                    string bodyReason;
                    string pqsType;
                    List<ModifierRecord> records = InspectBody(
                        body,
                        out bodyCapability,
                        out bodyReason,
                        out pqsType);
                    totalModifiers += records.Count;

                    switch (bodyCapability)
                    {
                        case Capability.NoPqs: noPqs++; break;
                        case Capability.PureCpuReady: pureCpuReady++; break;
                        case Capability.Snapshottable: snapshottable++; break;
                        case Capability.UnsupportedFormula: unsupported++; break;
                        case Capability.RuntimeDependent: runtimeDependent++; break;
                    }

                    EmitBody(body, pqsType, bodyCapability, bodyReason, records);
                }

                AERISLogger.Info(
                    "[R041A][ALL_BODY_PQS_CENSUS_COMPLETE]" +
                    "; pass=true" +
                    "; candidate=" + CandidateMarker +
                    "; bodies=" + bodies +
                    "; modifiers=" + totalModifiers +
                    "; pure_cpu_ready=" + pureCpuReady +
                    "; snapshottable=" + snapshottable +
                    "; unsupported_formula=" + unsupported +
                    "; runtime_dependent=" + runtimeDependent +
                    "; no_pqs=" + noPqs +
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
                    "[R041A][ALL_BODY_PQS_CENSUS_FAIL]" +
                    "; candidate=" + CandidateMarker +
                    "; error=" + Sanitize(ex.GetType().Name + ":" + ex.Message) +
                    "; production_authority=" + ProductionAuthority +
                    "; db_authority=" + DbAuthority +
                    "; producer_switch=" + ProducerSwitch +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false");
            }
        }

        static List<ModifierRecord> InspectBody(
            CelestialBody body,
            out Capability bodyCapability,
            out string bodyReason,
            out string pqsType)
        {
            var records = new List<ModifierRecord>();
            pqsType = "-";

            object pqs = null;
            try { pqs = body.pqsController; }
            catch { pqs = null; }

            if (pqs == null)
            {
                bodyCapability = Capability.NoPqs;
                bodyReason = "NO_PQS_CONTROLLER";
                return records;
            }

            pqsType = pqs.GetType().FullName ?? pqs.GetType().Name ?? "UNKNOWN";

            IList modifiers = GetModifierList(pqs);
            if (modifiers == null)
            {
                bodyCapability = Capability.RuntimeDependent;
                bodyReason = "PQS_MODIFIER_LIST_UNAVAILABLE";
                return records;
            }

            bool sawUnsupported = false;
            bool sawRuntime = false;
            bool sawSnapshottable = false;
            bool sawPure = false;

            for (int i = 0; i < modifiers.Count; i++)
            {
                object modifier = modifiers[i];
                if (modifier == null) continue;

                Type type = modifier.GetType();
                string typeName = type.FullName ?? type.Name ?? "UNKNOWN";
                string assembly = type.Assembly != null ? type.Assembly.GetName().Name : string.Empty;

                var record = new ModifierRecord
                {
                    Index = i,
                    TypeName = typeName,
                    AssemblyName = assembly ?? string.Empty
                };

                bool enabled;
                if (TryReadBool(modifier, "modEnabled", out enabled) ||
                    TryReadBool(modifier, "enabled", out enabled))
                {
                    record.HasEnabled = true;
                    record.Enabled = enabled;
                }

                int order;
                if (TryReadInt(modifier, "order", out order))
                {
                    record.HasOrder = true;
                    record.Order = order;
                }

                Capability capability;
                string reason;
                ClassifyModifier(body.name, typeName, assembly, out capability, out reason);
                record.Capability = capability;
                record.Reason = reason;
                records.Add(record);

                if (record.HasEnabled && !record.Enabled)
                    continue;

                if (capability == Capability.RuntimeDependent) sawRuntime = true;
                else if (capability == Capability.UnsupportedFormula) sawUnsupported = true;
                else if (capability == Capability.Snapshottable) sawSnapshottable = true;
                else if (capability == Capability.PureCpuReady) sawPure = true;
            }

            if (sawRuntime)
            {
                bodyCapability = Capability.RuntimeDependent;
                bodyReason = "ENABLED_RUNTIME_DEPENDENCY_REQUIRES_CLOSURE";
            }
            else if (sawUnsupported)
            {
                bodyCapability = Capability.UnsupportedFormula;
                bodyReason = "ENABLED_FORMULA_NOT_YET_CERTIFIED";
            }
            else if (sawSnapshottable)
            {
                bodyCapability = Capability.Snapshottable;
                bodyReason = "ENABLED_STATE_CAN_BE_SNAPSHOTTED_BUT_EXACT_EVALUATOR_INCOMPLETE";
            }
            else if (sawPure)
            {
                bodyCapability = Capability.PureCpuReady;
                bodyReason = "ALL_ENABLED_TERRAIN_FORMULAS_HAVE_ACCEPTED_PURE_CPU_PATH";
            }
            else
            {
                bodyCapability = Capability.Snapshottable;
                bodyReason = "PQS_PRESENT_NO_ENABLED_TERRAIN_MODIFIERS_DISCOVERED";
            }

            return records;
        }

        static void ClassifyModifier(
            string bodyName,
            string typeName,
            string assemblyName,
            out Capability capability,
            out string reason)
        {
            string name = typeName ?? string.Empty;

            if (name.EndsWith("PQSMod_VertexPlanet", StringComparison.Ordinal))
            {
                if (string.Equals(bodyName, "Minmus", StringComparison.OrdinalIgnoreCase))
                {
                    capability = Capability.PureCpuReady;
                    reason = "R039_R040B_MINMUS_EXACT_FAMILY_ACCEPTED";
                }
                else
                {
                    capability = Capability.Snapshottable;
                    reason = "VERTEXPLANET_FAMILY_EXISTS_BODY_CERTIFICATION_REQUIRED";
                }
                return;
            }

            for (int i = 0; i < KnownRuntimeDependentFragments.Length; i++)
            {
                if (name.IndexOf(KnownRuntimeDependentFragments[i],
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    capability = Capability.RuntimeDependent;
                    reason = "RUNTIME_GEOMETRY_OR_SCENE_DEPENDENCY_REQUIRES_CLOSURE";
                    return;
                }
            }

            for (int i = 0; i < KnownSnapshotAssetFragments.Length; i++)
            {
                if (name.IndexOf(KnownSnapshotAssetFragments[i],
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    capability = Capability.Snapshottable;
                    reason = "CONFIG_OR_ASSET_STATE_CAN_BE_COPIED_EXACT_EVALUATOR_REQUIRED";
                    return;
                }
            }

            if (!string.IsNullOrEmpty(assemblyName) &&
                !string.Equals(assemblyName, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
            {
                capability = Capability.UnsupportedFormula;
                reason = "NON_STOCK_MODIFIER_ASSEMBLY";
                return;
            }

            capability = Capability.UnsupportedFormula;
            reason = "PURE_CPU_FORMULA_NOT_YET_CERTIFIED";
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
                if (name.IndexOf("PQSMod", StringComparison.OrdinalIgnoreCase) >= 0)
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

        static void EmitBody(
            CelestialBody body,
            string pqsType,
            Capability capability,
            string reason,
            List<ModifierRecord> records)
        {
            string bodyName = Sanitize(body != null ? body.name : string.Empty);
            double radius = body != null ? body.Radius : 0.0;
            int enabled = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (!records[i].HasEnabled || records[i].Enabled) enabled++;
            }

            AERISLogger.Info(
                "[R041A][BODY]" +
                "; body=" + bodyName +
                "; radius_m=" + R(radius) +
                "; pqs_type=" + Sanitize(pqsType) +
                "; capability=" + CapabilityName(capability) +
                "; reason=" + Sanitize(reason) +
                "; modifiers=" + records.Count +
                "; enabled_modifiers=" + enabled +
                "; production_authority=" + ProductionAuthority +
                "; db_authority=" + DbAuthority +
                "; producer_switch=" + ProducerSwitch);

            for (int i = 0; i < records.Count; i++)
            {
                ModifierRecord record = records[i];
                var sb = new StringBuilder(256);
                sb.Append("[R041A][MODIFIER]");
                sb.Append("; body=").Append(bodyName);
                sb.Append("; index=").Append(record.Index);
                sb.Append("; type=").Append(Sanitize(record.TypeName));
                sb.Append("; assembly=").Append(Sanitize(record.AssemblyName));
                sb.Append("; capability=").Append(CapabilityName(record.Capability));
                sb.Append("; reason=").Append(Sanitize(record.Reason));
                if (record.HasEnabled)
                    sb.Append("; enabled=").Append(record.Enabled.ToString().ToLowerInvariant());
                else
                    sb.Append("; enabled=UNKNOWN");
                if (record.HasOrder)
                    sb.Append("; order=").Append(record.Order);
                else
                    sb.Append("; order=UNKNOWN");
                AERISLogger.Info(sb.ToString());
            }
        }

        static string CapabilityName(Capability capability)
        {
            switch (capability)
            {
                case Capability.NoPqs: return "NO_PQS";
                case Capability.PureCpuReady: return "PURE_CPU_READY";
                case Capability.Snapshottable: return "SNAPSHOTTABLE";
                case Capability.RuntimeDependent: return "RUNTIME_DEPENDENT";
                default: return "UNSUPPORTED_FORMULA";
            }
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
