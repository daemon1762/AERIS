namespace AERISFlightControl.FlightState
{
    // v0.4.27 read-only public flight-state contract.
    // Add-ons may observe these values; only AERIS core may command flight controls.
    public static class AERISFlightStateApi
    {
        static VirtualAttitudeInstrument current;
        internal static void Publish(VirtualAttitudeInstrument instrument) { current = instrument; }

        public static bool TryGetSnapshot(out AERISFlightStateSnapshot snapshot)
        {
            if (current == null || !current.InstrumentValid)
            {
                snapshot = default(AERISFlightStateSnapshot);
                return false;
            }

            snapshot = new AERISFlightStateSnapshot {
                AttitudeValid = current.InstrumentValid,
                Confidence = current.InstrumentConfidence,
                BankDeg = current.InstrumentBankDeg,
                BankWrappedDeg = current.InstrumentBankWrappedDeg,
                HorizonBankDeg = current.InstrumentHorizonBankDeg,
                HorizonBankValid = current.InstrumentHorizonBankValid,
                HorizonBankConfidence = current.InstrumentHorizonBankConfidence,
                PitchDeg = current.InstrumentPitchDeg,
                PitchValid = current.InstrumentPitchValid,
                HeadingDeg = current.InstrumentHeadingDeg,
                HeadingValid = current.InstrumentHeadingValid,
                RollRateDegPerSec = current.InstrumentRollRateDegPerSec,
                PitchRateDegPerSec = current.InstrumentPitchRateDegPerSec,
                YawRateDegPerSec = current.InstrumentYawRateDegPerSec,
                // v0.6.1 formal common kinematic baseline. These are native KSP primitives
                // proven equal to AA's equivalent values in Phase 2; they do not depend on AA.
                CommonKinematicBaselineValid = current.CommonKinematicBaselineValid,
                CommonKinematicBaselineSource = current.CommonKinematicBaselineSource,
                SharedSurfaceSpeedValid = current.SharedSurfaceSpeedValid,
                SharedRadarAltitudeValid = current.SharedRadarAltitudeValid,
                SharedDynamicPressureValid = current.SharedDynamicPressureValid,
                AltitudeAslSharedBaselineValid = current.AltitudeAslSharedBaselineValid,
                AltitudeAslSource = current.AltitudeAslSource,
                SurfaceSpeedMps = current.SurfaceSpeedMps,
                AltitudeAslM = current.AltitudeAslM,
                RadarAltitudeM = current.RadarAltitudeM,
                VerticalSpeedMps = current.VerticalSpeedMps,
                DynamicPressureKpa = current.DynamicPressureKpa,
                StaticPressureKpa = current.StaticPressureKpa,
                DensityKgM3 = current.DensityKgM3,
                GeeForce = current.GeeForce,
                HeadingStatus = current.InstrumentHeadingStatus
            };
            return true;
        }
    }

    public struct AERISFlightStateSnapshot
    {
        public bool AttitudeValid;
        public float Confidence;
        // Continuous bank for recorder/analysis; can exceed one revolution.
        public float BankDeg;
        // Player-facing BANK display and AP target basis: right positive, left negative, [-180,+180].
        public float BankWrappedDeg;
        public float HorizonBankDeg;
        public bool HorizonBankValid;
        public float HorizonBankConfidence;
        public float PitchDeg;
        public bool PitchValid;
        public float HeadingDeg;
        public bool HeadingValid;
        public float RollRateDegPerSec;
        public float PitchRateDegPerSec;
        public float YawRateDegPerSec;
        // Shared, native KSP kinematic baseline. This is deliberately independent of AA
        // availability and therefore safe for add-ons to observe at all flight stages.
        public bool CommonKinematicBaselineValid;
        public string CommonKinematicBaselineSource;
        public bool SharedSurfaceSpeedValid;
        public bool SharedRadarAltitudeValid;
        public bool SharedDynamicPressureValid;
        public bool AltitudeAslSharedBaselineValid;
        public string AltitudeAslSource;
        public float SurfaceSpeedMps;
        public float AltitudeAslM;
        public float RadarAltitudeM;
        public float VerticalSpeedMps;
        public float DynamicPressureKpa;
        public float StaticPressureKpa;
        public float DensityKgM3;
        public float GeeForce;
        // Diagnostic state for heading validity (e.g. VALID, undefined near vertical, undefined near pole).
        public string HeadingStatus;
    }
}
