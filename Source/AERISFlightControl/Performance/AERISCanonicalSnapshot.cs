using System;
using System.Threading;

namespace AERISFlightControl.Performance
{
    // Canonical, immutable display/AIFD snapshot. It carries only primitive values and
    // strings and cannot be used to write controls.
    public sealed class AERISCanonicalSnapshot
    {
        public AERISRuntimeGenerationStamp Generation { get; private set; }
        public double UniversalTime { get; private set; }
        public bool FlightStateValid { get; private set; }
        public double BankDeg { get; private set; }
        public double PitchDeg { get; private set; }
        public double HeadingDeg { get; private set; }
        public double SurfaceSpeedMps { get; private set; }
        public double VerticalSpeedMps { get; private set; }
        public double AltitudeAslMeters { get; private set; }
        public double RadarAltitudeMeters { get; private set; }
        public double DynamicPressureKpa { get; private set; }
        public string LateralMode { get; private set; }
        public string VerticalMode { get; private set; }
        public string SpeedMode { get; private set; }
        public string ProtectState { get; private set; }
        public string LandState { get; private set; }
        public string RunwayDirectionId { get; private set; }
        public double LocalizerDeviationMeters { get; private set; }
        public double GlidePathDeviationMeters { get; private set; }

        internal AERISCanonicalSnapshot(AERISRuntimeGenerationStamp generation,
            double universalTime, bool valid, double bank, double pitch, double heading,
            double speed, double verticalSpeed, double altitude, double radarAltitude,
            double dynamicPressure, string lateral, string vertical, string speedMode,
            string protect, string land, string runway, double localizer, double glidePath)
        {
            Generation = generation;
            UniversalTime = universalTime;
            FlightStateValid = valid;
            BankDeg = bank;
            PitchDeg = pitch;
            HeadingDeg = heading;
            SurfaceSpeedMps = speed;
            VerticalSpeedMps = verticalSpeed;
            AltitudeAslMeters = altitude;
            RadarAltitudeMeters = radarAltitude;
            DynamicPressureKpa = dynamicPressure;
            LateralMode = lateral ?? "NONE";
            VerticalMode = vertical ?? "NONE";
            SpeedMode = speedMode ?? "NONE";
            ProtectState = protect ?? "UNAVAILABLE";
            LandState = land ?? "OFF";
            RunwayDirectionId = runway ?? string.Empty;
            LocalizerDeviationMeters = localizer;
            GlidePathDeviationMeters = glidePath;
        }
    }

    public static class AERISCanonicalSnapshotApi
    {
        static AERISCanonicalSnapshot latest;
        internal static void Publish(AERISCanonicalSnapshot value)
        {
            Volatile.Write(ref latest, value);
        }
        internal static void Clear() { Volatile.Write(ref latest, null); }
        public static bool TryGetLatest(out AERISCanonicalSnapshot value)
        {
            value = Volatile.Read(ref latest);
            return value != null;
        }
    }
}
