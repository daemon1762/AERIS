using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 5 diagnostic only.
    //
    // This addon intentionally invokes the existing Phase 5 private BuildSamples
    // implementation by reflection instead of duplicating its sample generator.
    // It records the exact runtime values rejected by the same geodetic range
    // predicate used by the Phase 5 worker-parity observer. No PQS callbacks,
    // producer switches, DB writes, or preload mutations are performed here.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR042Phase5GeodeticCensusDiagnostic : MonoBehaviour
    {
        const string ObserverTypeName = "AERISR042ExactCpuShadowWorkerParityObserver";
        bool reported;

        void Start()
        {
            if (reported)
                return;
            reported = true;

            try
            {
                RunDiagnostic();
            }
            catch (Exception ex)
            {
                AERISLogger.Info(
                    "[AERIS42][R042_PHASE5_GEODETIC_DIAG_COMPLETE]" +
                    "; pass=false" +
                    "; failure=" + Safe(ex.GetType().Name + ":" + ex.Message) +
                    Invariants());
            }
        }

        static void RunDiagnostic()
        {
            Type observerType = typeof(AERISR042ExactCpuShadowWorkerParityObserver);
            const BindingFlags staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

            MethodInfo buildSamples = observerType.GetMethod(
                "BuildSamples", staticPrivate, null,
                new[] { typeof(string), typeof(int) }, null);
            MethodInfo nextDoubleUp = observerType.GetMethod(
                "NextDoubleUp", staticPrivate, null,
                new[] { typeof(double) }, null);

            if (buildSamples == null)
                throw new MissingMethodException(ObserverTypeName, "BuildSamples");
            if (nextDoubleUp == null)
                throw new MissingMethodException(ObserverTypeName, "NextDoubleUp");

            // modifierCount only seeds the random interior probes. The disputed
            // boundary and periodic samples are independent of this value, and
            // every random u/v is generated in [0,1), so cannot be rejected by
            // the geodetic range predicate.
            object rawSamples = buildSamples.Invoke(null, new object[] { "Kerbin", 1 });
            IEnumerable samples = rawSamples as IEnumerable;
            if (samples == null)
                throw new InvalidOperationException("BUILDSAMPLES_NOT_ENUMERABLE");

            int census = 0;
            int accepted = 0;
            int rejected = 0;

            foreach (object sample in samples)
            {
                if (sample == null)
                    throw new InvalidOperationException("NULL_SAMPLE");

                census++;
                string label = ReadString(sample, "Label");
                double u = ReadDouble(sample, "U");
                double v = ReadDouble(sample, "V");
                double latitude = ReadDouble(sample, "Latitude");
                double longitude = ReadDouble(sample, "Longitude");

                bool latLtMin = latitude < -90.0;
                bool latGtMax = latitude > 90.0;
                bool lonLtMin = longitude < -180.0;
                bool lonGtMax = longitude > 180.0;
                bool isRejected = latLtMin || latGtMax || lonLtMin || lonGtMax;

                if (!isRejected)
                {
                    accepted++;
                    continue;
                }

                rejected++;
                AERISLogger.Info(
                    "[AERIS42][R042_PHASE5_GEODETIC_REJECT]" +
                    "; index=" + (census - 1).ToString(CultureInfo.InvariantCulture) +
                    "; label=" + Safe(label) +
                    "; u=" + R(u) +
                    "; u_bits=" + Bits(u) +
                    "; v=" + R(v) +
                    "; v_bits=" + Bits(v) +
                    "; latitude=" + R(latitude) +
                    "; latitude_bits=" + Bits(latitude) +
                    "; longitude=" + R(longitude) +
                    "; longitude_bits=" + Bits(longitude) +
                    "; lat_lt_min=" + Bool(latLtMin) +
                    "; lat_gt_max=" + Bool(latGtMax) +
                    "; lon_lt_min=" + Bool(lonLtMin) +
                    "; lon_gt_max=" + Bool(lonGtMax) +
                    Invariants());
            }

            double upOne = Convert.ToDouble(
                nextDoubleUp.Invoke(null, new object[] { 1.0 }),
                CultureInfo.InvariantCulture);
            double upperLatitude = (upOne - 0.5) * 180.0;
            double upperLongitude = (upOne - 0.5) * 360.0;

            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_FP_BOUNDARY_PROBE]" +
                "; one_bits=" + Bits(1.0) +
                "; next_up_one=" + R(upOne) +
                "; next_up_one_bits=" + Bits(upOne) +
                "; derived_latitude=" + R(upperLatitude) +
                "; derived_latitude_bits=" + Bits(upperLatitude) +
                "; derived_longitude=" + R(upperLongitude) +
                "; derived_longitude_bits=" + Bits(upperLongitude) +
                "; latitude_gt_90=" + Bool(upperLatitude > 90.0) +
                "; longitude_gt_180=" + Bool(upperLongitude > 180.0) +
                Invariants());

            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_GEODETIC_DIAG_COMPLETE]" +
                "; pass=true" +
                "; census=" + census.ToString(CultureInfo.InvariantCulture) +
                "; accepted=" + accepted.ToString(CultureInfo.InvariantCulture) +
                "; rejected=" + rejected.ToString(CultureInfo.InvariantCulture) +
                "; r041_runtime_expected_accepted=560" +
                "; phase5_source_expected_accepted=560" +
                "; sample_generator=PHASE5_BUILDSAMPLES_REFLECTION_EXACT" +
                "; range_predicate=LAT_LON_INCLUSIVE_STOCK_GEODETIC" +
                Invariants());
        }

        static object ReadField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        static string ReadString(object target, string name)
        {
            object value = ReadField(target, name);
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static double ReadDouble(object target, string name)
        {
            return Convert.ToDouble(ReadField(target, name), CultureInfo.InvariantCulture);
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Bits(double value)
        {
            return unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString(
                "X16", CultureInfo.InvariantCulture);
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "NONE";
            return value.Replace(';', '_').Replace('\r', '_').Replace('\n', '_');
        }

        static string Invariants()
        {
            return
                "; diagnostic_only=true" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false";
        }
    }
}
