using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS34_REV3_5_R039_MINMUS_LIBNOISE_STATIC_CLOSURE_SHADOW
    //
    // Shadow-only runtime observation.
    // No terrain DB write.
    // No producer switch.
    // No GPU path.
    // No worker runtime-object access.

    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class
        AERISR039PtcMinmusLibNoiseStaticClosureObserver
        : MonoBehaviour
    {
        const string CandidateMarker =
            "AERIS34_REV3_5_R039_MINMUS_LIBNOISE_STATIC_CLOSURE_SHADOW";

        float nextAttempt;
        bool captured;

        void Update()
        {
            if (captured)
                return;

            if (Time.realtimeSinceStartup < nextAttempt)
                return;

            nextAttempt =
                Time.realtimeSinceStartup + 1f;

            if (!AERISTerrainTileSystem.GameDataHashReady)
                return;

            if (FlightGlobals.Bodies == null ||
                FlightGlobals.Bodies.Count == 0)
                return;

            Capture();
            captured = true;
        }

        void Capture()
        {
            int failures = 0;

            CelestialBody minmus =
                FindBody("Minmus");

            if (minmus == null)
            {
                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_FAIL]" +
                    "; stage=BODY" +
                    "; error=MINMUS_NOT_FOUND"
                );
                return;
            }

            object pqs =
                ReadMember(
                    minmus,
                    "pqsController"
                );

            object vertexPlanet =
                FindMod(
                    pqs,
                    "PQSMod_VertexPlanet"
                );

            if (vertexPlanet == null)
            {
                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_FAIL]" +
                    "; stage=VERTEXPLANET" +
                    "; error=NULL"
                );
                return;
            }

            object wrapper =
                ReadMember(
                    vertexPlanet,
                    "continentalSharpness"
                );

            object noise =
                ReadMember(
                    wrapper,
                    "noise"
                );

            if (noise == null)
            {
                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_FAIL]" +
                    "; stage=RIDGED_MULTIFRACTAL" +
                    "; error=NULL"
                );
                return;
            }

            AERISLogger.Info(
                "[R039][LIBNOISE_STATIC_BEGIN]" +
                "; body=Minmus" +
                "; candidate=" + CandidateMarker +
                "; primitive_type=" +
                    Safe(noise.GetType().FullName) +
                "; runtime_object_invocation_thread=" +
                    "MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY" +
                "; db_write=false" +
                "; producer_switch=false" +
                "; gpu=false" +
                "; authority=PQS"
            );

            // Capture NoiseQuality in both symbolic and numeric form.
            try
            {
                object quality =
                    ReadMember(
                        noise,
                        "NoiseQuality"
                    );

                int numeric =
                    Convert.ToInt32(
                        quality,
                        CultureInfo.InvariantCulture
                    );

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_ENUM]" +
                    "; label=continentalSharpness" +
                    "; name=NoiseQuality" +
                    "; symbolic=" +
                        Safe(
                            quality == null
                                ? string.Empty
                                : quality.ToString()
                        ) +
                    "; numeric=" +
                        numeric.ToString(
                            CultureInfo.InvariantCulture
                        )
                );
            }
            catch (Exception ex)
            {
                failures++;

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_FAIL]" +
                    "; stage=NOISE_QUALITY" +
                    "; error=" +
                        Safe(ex.GetType().Name)
                );
            }

            // GradientNoiseBasis.RandomVectors is the final
            // evaluation-time static dependency of
            // RidgedMultifractal -> GradientNoise.
            try
            {
                Assembly assembly =
                    noise.GetType().Assembly;

                Type basis =
                    assembly.GetType(
                        "LibNoise.GradientNoiseBasis",
                        false
                    );

                if (basis == null)
                    throw new InvalidOperationException(
                        "GradientNoiseBasis type missing"
                    );

                FieldInfo field =
                    basis.GetField(
                        "RandomVectors",
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic
                    );

                if (field == null)
                    throw new MissingFieldException(
                        "RandomVectors"
                    );

                double[] values =
                    field.GetValue(null)
                    as double[];

                if (values == null)
                    throw new InvalidCastException(
                        "RandomVectors is not double[]"
                    );

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_ARRAY]" +
                    "; declaring_type=" +
                        Safe(basis.FullName) +
                    "; name=RandomVectors" +
                    "; element_type=System.Double" +
                    "; length=" +
                        values.Length.ToString(
                            CultureInfo.InvariantCulture
                        ) +
                    "; bit_sha256=" +
                        Sha256DoubleBits(values)
                );

                const int PerChunk = 24;

                for (
                    int start = 0, chunk = 0;
                    start < values.Length;
                    start += PerChunk, chunk++
                )
                {
                    int count =
                        Math.Min(
                            PerChunk,
                            values.Length - start
                        );

                    string[] parts =
                        new string[count];

                    for (int i = 0; i < count; i++)
                    {
                        int index =
                            start + i;

                        parts[i] =
                            index.ToString(
                                CultureInfo.InvariantCulture
                            ) +
                            ":" +
                            values[index].ToString(
                                "R",
                                CultureInfo.InvariantCulture
                            );
                    }

                    AERISLogger.Info(
                        "[R039][LIBNOISE_STATIC_ARRAY_CHUNK]" +
                        "; name=RandomVectors" +
                        "; chunk=" +
                            chunk.ToString(
                                CultureInfo.InvariantCulture
                            ) +
                        "; values=" +
                            Safe(
                                string.Join(
                                    "~",
                                    parts
                                )
                            )
                    );
                }

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_COMPLETE]" +
                    "; body=Minmus" +
                    "; random_vectors=" +
                        values.Length.ToString(
                            CultureInfo.InvariantCulture
                        ) +
                    "; failures=" +
                        failures.ToString(
                            CultureInfo.InvariantCulture
                        ) +
                    "; worker_ready=false" +
                    "; pending=PURE_CPU_PRIMITIVE_RECONSTRUCTION" +
                    "; runtime_object_invocation_thread=" +
                        "MAIN_THREAD_ONLY" +
                    "; worker_invokes_runtime_object=false" +
                    "; certification=NO_SHADOW_ONLY" +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; gpu=false" +
                    "; authority=PQS"
                );
            }
            catch (Exception ex)
            {
                failures++;

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_FAIL]" +
                    "; stage=RANDOM_VECTORS" +
                    "; error=" +
                        Safe(ex.GetType().Name) +
                    "; message=" +
                        Safe(ex.Message)
                );

                AERISLogger.Info(
                    "[R039][LIBNOISE_STATIC_COMPLETE]" +
                    "; body=Minmus" +
                    "; random_vectors=0" +
                    "; failures=" +
                        failures.ToString(
                            CultureInfo.InvariantCulture
                        ) +
                    "; worker_ready=false" +
                    "; pending=PURE_CPU_PRIMITIVE_RECONSTRUCTION" +
                    "; runtime_object_invocation_thread=" +
                        "MAIN_THREAD_ONLY" +
                    "; worker_invokes_runtime_object=false" +
                    "; certification=NO_SHADOW_ONLY" +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; gpu=false" +
                    "; authority=PQS"
                );
            }
        }

        static string Sha256DoubleBits(
            double[] values
        )
        {
            byte[] bytes =
                new byte[
                    values.Length * sizeof(double)
                ];

            Buffer.BlockCopy(
                values,
                0,
                bytes,
                0,
                bytes.Length
            );

            using (
                SHA256 sha =
                    SHA256.Create()
            )
            {
                byte[] hash =
                    sha.ComputeHash(bytes);

                StringBuilder text =
                    new StringBuilder(
                        hash.Length * 2
                    );

                for (
                    int i = 0;
                    i < hash.Length;
                    i++
                )
                {
                    text.Append(
                        hash[i].ToString(
                            "x2",
                            CultureInfo.InvariantCulture
                        )
                    );
                }

                return text.ToString();
            }
        }

        static CelestialBody FindBody(
            string name
        )
        {
            for (
                int i = 0;
                i < FlightGlobals.Bodies.Count;
                i++
            )
            {
                CelestialBody body =
                    FlightGlobals.Bodies[i];

                if (
                    body != null &&
                    string.Equals(
                        body.name,
                        name,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return body;
                }
            }

            return null;
        }

        static object FindMod(
            object pqs,
            string runtimeType
        )
        {
            if (pqs == null)
                return null;

            IEnumerable mods =
                ReadMember(
                    pqs,
                    "mods"
                )
                as IEnumerable;

            if (mods == null)
                return null;

            foreach (object mod in mods)
            {
                if (mod == null)
                    continue;

                Type type =
                    mod.GetType();

                string name =
                    type.FullName ??
                    type.Name;

                if (
                    string.Equals(
                        name,
                        runtimeType,
                        StringComparison.Ordinal
                    )
                )
                {
                    return mod;
                }
            }

            return null;
        }

        static object ReadMember(
            object target,
            string name
        )
        {
            if (target == null)
                return null;

            Type type =
                target.GetType();

            FieldInfo field =
                type.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

            if (field != null)
            {
                return field.GetValue(
                    field.IsStatic
                        ? null
                        : target
                );
            }

            PropertyInfo property =
                type.GetProperty(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

            if (
                property != null &&
                property.CanRead &&
                property
                    .GetIndexParameters()
                    .Length == 0
            )
            {
                return property.GetValue(
                    target,
                    null
                );
            }

            return null;
        }

        static string Safe(
            string value
        )
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace(';', ',')
                .Replace('|', '/');
        }
    }
}
