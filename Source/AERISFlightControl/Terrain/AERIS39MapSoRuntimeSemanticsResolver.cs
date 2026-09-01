using System;
using System.Collections;
using System.Reflection;

namespace AERISFlightControl.Terrain
{
    // Main-thread-only runtime semantics resolver for MAPSO-3J witness capture.
    // It does not invoke MapSO sampling helpers. It inspects Harmony patch state
    // and KSPCommunityFixes' own per-instance exception references, then reduces
    // the result to a primitive enum stored in the worker snapshot.
    internal static class AERIS39MapSoRuntimeSemanticsResolver
    {
        const string KspcfPatchType = "KSPCommunityFixes.MapSOCorrectWrapping";
        const string KspcfDoublePrefix = "MapSO_ConstructBilinearCoords_Double";

        internal static AERIS39MapSoPureCpuExact.CoordinateSemantics Resolve(
            MapSO map,
            out string evidence)
        {
            if (map == null) throw new ArgumentNullException("map");

            MethodInfo target = typeof(MapSO).GetMethod(
                "ConstructBilinearCoords",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(double), typeof(double) },
                null);
            if (target == null)
                throw new MissingMethodException(typeof(MapSO).FullName, "ConstructBilinearCoords(double,double)");

            MethodBase prefix = FindKspcfPrefix(target);
            if (prefix == null)
            {
                evidence = "STOCK_NO_KSPCF_PREFIX";
                return AERIS39MapSoPureCpuExact.CoordinateSemantics.StockWrapXY;
            }

            Type patchType = prefix.DeclaringType;
            if (patchType == null ||
                !string.Equals(patchType.FullName, KspcfPatchType, StringComparison.Ordinal))
                throw new InvalidOperationException("KSPCF_PREFIX_DECLARING_TYPE_UNEXPECTED");

            object mohoBiomes = ReadStaticField(patchType, "moho_biomes");
            object mohoHeight = ReadStaticField(patchType, "moho_height");

            if (object.ReferenceEquals(map, mohoBiomes))
            {
                evidence = "STOCK_KSPCF_INSTANCE_EXCEPTION_BIOMES";
                return AERIS39MapSoPureCpuExact.CoordinateSemantics.StockWrapXY;
            }

            if (object.ReferenceEquals(map, mohoHeight))
            {
                evidence = "STOCK_KSPCF_INSTANCE_EXCEPTION_HEIGHT";
                return AERIS39MapSoPureCpuExact.CoordinateSemantics.StockWrapXY;
            }

            evidence = "KSPCF_CORRECT_WRAPPING_PREFIX_ACTIVE";
            return AERIS39MapSoPureCpuExact.CoordinateSemantics.KspCommunityFixesWrapXClampY;
        }

        static MethodBase FindKspcfPrefix(MethodBase target)
        {
            Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
            if (harmonyType == null) return null;

            MethodInfo getPatchInfo = null;
            MethodInfo[] methods = harmonyType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "GetPatchInfo", StringComparison.Ordinal)) continue;
                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 1) continue;
                if (!ps[0].ParameterType.IsAssignableFrom(target.GetType()) &&
                    !ps[0].ParameterType.IsAssignableFrom(typeof(MethodBase)) &&
                    !typeof(MethodBase).IsAssignableFrom(ps[0].ParameterType))
                    continue;
                getPatchInfo = method;
                break;
            }
            if (getPatchInfo == null)
                throw new MissingMethodException(harmonyType.FullName, "GetPatchInfo(MethodBase)");

            object patchInfo = getPatchInfo.Invoke(null, new object[] { target });
            if (patchInfo == null) return null;

            object prefixes = ReadInstanceMember(patchInfo, "Prefixes") ??
                ReadInstanceMember(patchInfo, "prefixes");
            IEnumerable enumerable = prefixes as IEnumerable;
            if (enumerable == null) return null;

            foreach (object patch in enumerable)
            {
                MethodBase patchMethod = FindMethodBaseMember(patch);
                if (patchMethod == null || patchMethod.DeclaringType == null) continue;
                if (!string.Equals(patchMethod.DeclaringType.FullName, KspcfPatchType, StringComparison.Ordinal)) continue;
                if (!string.Equals(patchMethod.Name, KspcfDoublePrefix, StringComparison.Ordinal)) continue;
                return patchMethod;
            }

            return null;
        }

        static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        static MethodBase FindMethodBaseMember(object target)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo[] properties = type.GetProperties(flags);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
                if (!typeof(MethodBase).IsAssignableFrom(property.PropertyType)) continue;
                try
                {
                    MethodBase value = property.GetValue(target, null) as MethodBase;
                    if (value != null) return value;
                }
                catch { }
            }

            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!typeof(MethodBase).IsAssignableFrom(field.FieldType)) continue;
                try
                {
                    MethodBase value = field.GetValue(target) as MethodBase;
                    if (value != null) return value;
                }
                catch { }
            }

            return null;
        }

        static object ReadInstanceMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try { return property.GetValue(target, null); }
                catch { }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try { return field.GetValue(target); }
                catch { }
            }

            return null;
        }

        static object ReadStaticField(Type type, string name)
        {
            if (type == null) return null;
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return null;
            try { return field.GetValue(null); }
            catch { return null; }
        }
    }
}
