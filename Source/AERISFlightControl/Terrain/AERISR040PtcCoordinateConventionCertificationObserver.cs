using System;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS35 R040-1
    //
    // Shadow-only certification of the exact latitude/longitude -> PQS unit-vector
    // convention used by KSP CelestialBody.GetRelSurfaceNVector().
    //
    // No database writes.
    // No producer switch.
    // No preload mutation.
    // No worker access to KSP/Unity runtime objects.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR040PtcCoordinateConventionCertificationObserver
        : MonoBehaviour
    {
        const string CandidateMarker =
            "AERIS35_R040_PTC_COORDINATE_CONVENTION_CERTIFICATION_SHADOW_HOTFIX1";

        const double CoordinateTolerance = 1E-12;
        const double UniquenessFloor = 1E-6;

        static readonly string[] TargetBodies =
            new string[] { "Gilly", "Ike", "Pol", "Minmus" };

        struct Witness
        {
            internal readonly double Latitude;
            internal readonly double Longitude;

            internal Witness(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }
        }

        static readonly Witness[] Witnesses = new Witness[]
        {
            new Witness(0.0, 0.0),
            new Witness(0.0, 90.0),
            new Witness(0.0, -90.0),
            new Witness(0.0, 180.0),
            new Witness(90.0, 0.0),
            new Witness(-90.0, 0.0),

            new Witness(12.345, 67.89),
            new Witness(-34.5, 123.4),
            new Witness(48.123456789, -72.987654321),
            new Witness(-5.25, -179.875),
            new Witness(71.2345, 18.7654),
            new Witness(-63.8765, 42.1357)
        };

        sealed class Candidate
        {
            internal int P0;
            internal int P1;
            internal int P2;

            internal int S0;
            internal int S1;
            internal int S2;

            internal double MaximumError;

            internal string Label
            {
                get
                {
                    return
                        "X=" + SignedName(S0, P0) +
                        ",Y=" + SignedName(S1, P1) +
                        ",Z=" + SignedName(S2, P2);
                }
            }
        }

        float nextAttempt;
        bool reported;

        void Update()
        {
            if (reported)
                return;

            if (Time.realtimeSinceStartup < nextAttempt)
                return;

            nextAttempt = Time.realtimeSinceStartup + 1f;

            if (FlightGlobals.Bodies == null ||
                FlightGlobals.Bodies.Count == 0)
                return;

            RunCertification();
        }

        void RunCertification()
        {
            reported = true;

            try
            {
                string acceptedMapping = null;
                bool allPass = true;
                double globalMaximumError = 0.0;
                double minimumSecondBestError = double.PositiveInfinity;

                AERISLogger.Info(
                    "[R040][COORD_BEGIN]" +
                    "; candidate=" + CandidateMarker +
                    "; bodies=" + TargetBodies.Length +
                    "; witnesses=" + Witnesses.Length +
                    "; tolerance=" + R(CoordinateTolerance) +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; preload_mutation=false" +
                    "; authority=CelestialBody.GetRelSurfaceNVector");

                for (int bodyIndex = 0;
                     bodyIndex < TargetBodies.Length;
                     bodyIndex++)
                {
                    string bodyName = TargetBodies[bodyIndex];
                    CelestialBody body = FindBody(bodyName);

                    if (body == null)
                    {
                        allPass = false;

                        AERISLogger.Info(
                            "[R040][COORD_BODY]" +
                            "; body=" + bodyName +
                            "; pass=false" +
                            "; error=BODY_NOT_FOUND");

                        continue;
                    }

                    Candidate best;
                    Candidate second;

                    EvaluateCandidates(body, out best, out second);

                    bool unique =
                        second != null &&
                        second.MaximumError >= UniquenessFloor;

                    bool pass =
                        best != null &&
                        best.MaximumError <= CoordinateTolerance &&
                        unique;

                    if (!pass)
                        allPass = false;

                    if (best != null)
                    {
                        globalMaximumError = Math.Max(
                            globalMaximumError,
                            best.MaximumError);

                        if (acceptedMapping == null)
                            acceptedMapping = best.Label;
                        else if (!string.Equals(
                            acceptedMapping,
                            best.Label,
                            StringComparison.Ordinal))
                            allPass = false;
                    }

                    if (second != null)
                        minimumSecondBestError = Math.Min(
                            minimumSecondBestError,
                            second.MaximumError);

                    AERISLogger.Info(
                        "[R040][COORD_BODY]" +
                        "; body=" + bodyName +
                        "; pass=" + pass +
                        "; mapping=" +
                            Safe(best == null ? "<none>" : best.Label) +
                        "; max_component_error=" +
                            R(best == null
                                ? double.PositiveInfinity
                                : best.MaximumError) +
                        "; second_best_error=" +
                            R(second == null
                                ? double.PositiveInfinity
                                : second.MaximumError) +
                        "; unique=" + unique +
                        "; witness_count=" + Witnesses.Length +
                        "; authority=CelestialBody.GetRelSurfaceNVector");
                }

                if (string.IsNullOrEmpty(acceptedMapping))
                    allPass = false;

                AERISLogger.Info(
                    "[R040][COORD_COMPLETE]" +
                    "; pass=" + allPass +
                    "; mapping=" +
                        Safe(acceptedMapping ?? "<none>") +
                    "; bodies=" + TargetBodies.Length +
                    "; witnesses_per_body=" + Witnesses.Length +
                    "; max_component_error=" +
                        R(globalMaximumError) +
                    "; minimum_second_best_error=" +
                        R(minimumSecondBestError) +
                    "; coordinate_tolerance=" +
                        R(CoordinateTolerance) +
                    "; uniqueness_floor=" +
                        R(UniquenessFloor) +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; preload_mutation=false" +
                    "; certification=" +
                        (allPass
                            ? "COORDINATE_CONVENTION_CERTIFIED"
                            : "NO_FAIL_CLOSED") +
                    "; authority=CelestialBody.GetRelSurfaceNVector");
            }
            catch (Exception ex)
            {
                AERISLogger.Info(
                    "[R040][COORD_FAIL]" +
                    "; error=" + Safe(ex.GetType().Name) +
                    "; message=" + Safe(ex.Message) +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; preload_mutation=false" +
                    "; certification=NO_FAIL_CLOSED");
            }
        }

        static void EvaluateCandidates(
            CelestialBody body,
            out Candidate best,
            out Candidate second)
        {
            best = null;
            second = null;

            int[][] permutations = new int[][]
            {
                new int[] { 0, 1, 2 },
                new int[] { 0, 2, 1 },
                new int[] { 1, 0, 2 },
                new int[] { 1, 2, 0 },
                new int[] { 2, 0, 1 },
                new int[] { 2, 1, 0 }
            };

            for (int p = 0; p < permutations.Length; p++)
            {
                int[] permutation = permutations[p];

                for (int signs = 0; signs < 8; signs++)
                {
                    Candidate candidate = new Candidate
                    {
                        P0 = permutation[0],
                        P1 = permutation[1],
                        P2 = permutation[2],

                        S0 = (signs & 1) == 0 ? 1 : -1,
                        S1 = (signs & 2) == 0 ? 1 : -1,
                        S2 = (signs & 4) == 0 ? 1 : -1,

                        MaximumError = 0.0
                    };

                    for (int i = 0; i < Witnesses.Length; i++)
                    {
                        Witness witness = Witnesses[i];

                        Vector3d actual = body.GetRelSurfaceNVector(
                            witness.Latitude,
                            witness.Longitude);

                        double latitudeRad =
                            witness.Latitude * Math.PI / 180.0;

                        double longitudeRad =
                            witness.Longitude * Math.PI / 180.0;

                        double cosineLatitude = Math.Cos(latitudeRad);

                        double[] canonical = new double[]
                        {
                            cosineLatitude * Math.Cos(longitudeRad),
                            cosineLatitude * Math.Sin(longitudeRad),
                            Math.Sin(latitudeRad)
                        };

                        double expectedX =
                            candidate.S0 * canonical[candidate.P0];

                        double expectedY =
                            candidate.S1 * canonical[candidate.P1];

                        double expectedZ =
                            candidate.S2 * canonical[candidate.P2];

                        double error = Math.Max(
                            Math.Abs(actual.x - expectedX),
                            Math.Max(
                                Math.Abs(actual.y - expectedY),
                                Math.Abs(actual.z - expectedZ)));

                        candidate.MaximumError = Math.Max(
                            candidate.MaximumError,
                            error);
                    }

                    if (best == null ||
                        candidate.MaximumError < best.MaximumError)
                    {
                        second = best;
                        best = candidate;
                    }
                    else if (second == null ||
                        candidate.MaximumError < second.MaximumError)
                    {
                        second = candidate;
                    }
                }
            }
        }

        static CelestialBody FindBody(string name)
        {
            if (FlightGlobals.Bodies == null)
                return null;

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];

                if (body != null &&
                    string.Equals(
                        body.name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    return body;
            }

            return null;
        }

        static string SignedName(int sign, int index)
        {
            string name =
                index == 0 ? "COS_LAT_COS_LON" :
                index == 1 ? "COS_LAT_SIN_LON" :
                "SIN_LAT";

            return sign < 0 ? "-" + name : name;
        }

        static string R(double value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";

            return value
                .Replace(';', '_')
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }
    }
}
