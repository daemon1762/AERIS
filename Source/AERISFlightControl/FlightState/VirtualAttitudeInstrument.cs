using UnityEngine;

namespace AERISFlightControl.FlightState
{
    // v0.4.27: read-only virtual attitude instrument initial-completion core and formal attitude telemetry.
    // It intentionally does not write FlightCtrlState or modify AA.
    internal sealed class VirtualAttitudeInstrument
    {
        internal bool Valid { get; private set; }
        internal float Confidence { get; private set; }
        // Primary raw KSP orientation observation. Retained for compatibility with v0.4.13b.
        internal float BankDeg { get; private set; }
        internal float PitchDeg { get; private set; }
        internal float HeadingDeg { get; private set; }
        internal bool PitchValid { get; private set; }
        internal bool HeadingValid { get; private set; }
        // Private validation comparison reference. Never used for control or exposed in the formal API/FDR/UI contract.
        internal float VanillaNavballHeadingDeg { get; private set; }
        internal bool VanillaNavballHeadingValid { get; private set; }
        internal float HeadingErrorDeg { get; private set; }
        // Human-readable validity reason for UI/FDR diagnostics.
        internal string HeadingStatus { get; private set; }
        internal float RollRateDegPerSec { get; private set; }
        internal float PitchRateDegPerSec { get; private set; }
        internal float YawRateDegPerSec { get; private set; }
        // v0.6.3 formal shared-native acceptance boundary. Phase 2 confirmed exact equality
        // only for surface speed, radar altitude and dynamic pressure. Altitude ASL is deliberately
        // excluded: AA's sampling may lag one FixedUpdate during rapid vertical motion.
        // All accepted values remain sourced from native KSP state, never copied from AA FlightModel.
        internal bool CommonKinematicBaselineValid { get; private set; }
        internal string CommonKinematicBaselineSource { get; private set; } = "UNAVAILABLE";
        internal bool SharedSurfaceSpeedValid { get; private set; }
        internal bool SharedRadarAltitudeValid { get; private set; }
        internal bool SharedDynamicPressureValid { get; private set; }
        internal bool AltitudeAslValid { get; private set; }
        internal bool VerticalSpeedValid { get; private set; }
        internal bool AltitudeAslSharedBaselineValid { get; private set; }
        internal string AltitudeAslSource { get; private set; } = "UNAVAILABLE";
        internal float SurfaceSpeedMps { get; private set; }
        internal float AltitudeAslM { get; private set; }
        internal float RadarAltitudeM { get; private set; }
        internal float VerticalSpeedMps { get; private set; }
        internal float DynamicPressureKpa { get; private set; }
        internal float StaticPressureKpa { get; private set; }
        internal float DensityKgM3 { get; private set; }
        internal float GeeForce { get; private set; }
        internal Vector3 GravityUp { get; private set; }
        internal Vector3 SurfaceVelocityDirection { get; private set; }


        // v0.5.7 observation-only FlightState crosscheck values. These are never consumed by
        // AERIS directors, Protect, or AA. They exist solely so the recorder can compare
        // independently derived AERIS geometric observations with AA FlightModel values.
        internal bool EstimatedAoAValid { get; private set; }
        internal float EstimatedPitchAoADeg { get; private set; }
        internal float EstimatedRollAoADeg { get; private set; }
        internal float EstimatedYawAoADeg { get; private set; }

        internal bool InstrumentAngularAccelerationValid { get; private set; }
        internal float InstrumentRollAccelerationDegPerSec2 { get; private set; }
        internal float InstrumentPitchAccelerationDegPerSec2 { get; private set; }
        internal float InstrumentYawAccelerationDegPerSec2 { get; private set; }
        internal float LastSampleFixedTime { get; private set; } = -1f;
        internal float SampleAgeSeconds
        {
            get { return LastSampleFixedTime < 0f ? float.PositiveInfinity : Mathf.Max(0f, Time.fixedTime - LastSampleFixedTime); }
        }

        // Calibration-only derived observations. No one is allowed to use these for AP yet.
        internal bool GravityFrameValid { get; private set; }
        internal Quaternion RawQuaternion { get; private set; }
        internal float TransformForwardGravityDot { get; private set; }
        internal float TransformUpGravityDot { get; private set; }
        internal float TransformRightGravityDot { get; private set; }
        internal float BankAroundForwardDeg { get; private set; }
        internal float BankAroundUpDeg { get; private set; }
        internal float BankAroundRightDeg { get; private set; }
        internal float DerivedBankConfidence { get; private set; }

        // v0.4.13b explicit AERIS Body Frame. This convention is fixed for calibration only:
        // longitudinal = KSP vessel.transform.up, lateral = transform.right,
        // vertical/up = -transform.forward. It is not yet claimed to match the Navball.
        internal bool BodyFrameValid { get; private set; }
        internal float BodyLongitudinalGravityDot { get; private set; }
        internal float BodyLateralGravityDot { get; private set; }
        internal float BodyVerticalGravityDot { get; private set; }
        internal float BodyFrameBankDeg { get; private set; }
        internal float BodyFramePitchDeg { get; private set; }
        internal float BodyFrameHeadingDeg { get; private set; }
        internal float BodyFrameConfidence { get; private set; }

        // v0.4.13b: direct gravity projections. These explicitly separate the raw
        // virtual-sensor sample from the body-frame attitude estimate. Observe only.
        internal float GravityProjectionBankDeg { get; private set; }
        internal float GravityProjectionPitchDeg { get; private set; }
        internal bool GravityProjectionValid { get; private set; }
        internal float GravityProjectionConfidence { get; private set; }
        // v0.4.13b Control-point/reference-frame calibration. Observe only.
        // Vessel.ReferenceTransform is KSP's current control/reference orientation; these fields
        // expose it without assuming it equals vessel/root visual axes.
        internal bool ControlFrameValid { get; private set; }
        internal string ControlFrameName { get; private set; }
        internal float ControlForwardGravityDot { get; private set; }
        internal float ControlUpGravityDot { get; private set; }
        internal float ControlRightGravityDot { get; private set; }
        internal float ControlVsVesselRotationDeg { get; private set; }
        internal float ControlVsRootRotationDeg { get; private set; }
        internal float ControlBankAboutForwardDeg { get; private set; }
        internal float ControlBankAboutUpDeg { get; private set; }
        internal float ControlBankAboutRightDeg { get; private set; }
        internal float ControlFrameConfidence { get; private set; }

        // v0.4.13b legacy observe-only candidate. This is deliberately separate
        // from BankDeg and all legacy candidates. It is the sign-normalized form of
        // the v0.4.4 control-frame right-axis trace and is not available to AP.
        internal bool ControlBankCandidateValid { get; private set; }
        internal float ControlBankCandidateDeg { get; private set; }
        internal float ControlBankCandidateConfidence { get; private set; }

        // v0.4.13b legacy quaternion calibration trace. The wrapped value is the control-point candidate
        // normalized to [-180,+180]. The unwrapped value integrates shortest signed changes
        // between samples, so aerobatic rolls remain continuous beyond one revolution.
        // Read-only: neither value is available to BANK/AP control in this calibration build.
        internal bool ControlBankUnwrappedValid { get; private set; }
        internal float ControlBankWrappedDeg { get; private set; }
        internal float ControlBankUnwrappedDeg { get; private set; }
        internal float ControlBankUnwrappedDeltaDeg { get; private set; }
        internal float ControlBankUnwrappedConfidence { get; private set; }

        // v0.4.13b legacy gyro-axis calibration trace. These are the world angular velocity
        // projected into KSP's active control-point axes. No axis is declared as the
        // final AERIS RollRate yet; the FDR records all three so manual roll/pitch/yaw
        // maneuvers can identify the correct mapping empirically.
        internal bool ControlGyroValid { get; private set; }
        internal float ControlGyroForwardRateDegPerSec { get; private set; }
        internal float ControlGyroUpRateDegPerSec { get; private set; }
        internal float ControlGyroRightRateDegPerSec { get; private set; }
        internal float ControlGyroMagnitudeDegPerSec { get; private set; }

        // v0.4.13b legacy quaternion calibration trace. v0.4.13b previously identified the active
        // control-frame right axis as the manual roll-rate sensor. This tracker integrates
        // that rate at Unity update cadence; it is observe-only and intentionally receives
        // no gravity/legacy-bank correction yet.
        internal bool GyroIntegratedBankValid { get; private set; }
        internal float GyroIntegratedBankDeg { get; private set; }
        internal float GyroIntegratedBankWrappedDeg { get; private set; }
        internal float GyroIntegratedBankDeltaDeg { get; private set; }
        internal float GyroIntegratedBankConfidence { get; private set; }

        // v0.4.13b legacy quaternion calibration trace. Each increment is expressed in
        // the previous control-point local frame. This is diagnostics only: do not use
        // any axis for BANK/AP until manual roll, pitch, and yaw tests identify the mapping.
        internal bool QuaternionDeltaValid { get; private set; }
        internal float QuaternionDeltaLocalXDeg { get; private set; }
        internal float QuaternionDeltaLocalYDeg { get; private set; }
        internal float QuaternionDeltaLocalZDeg { get; private set; }
        internal float QuaternionDeltaLocalXRateDegPerSec { get; private set; }
        internal float QuaternionDeltaLocalYRateDegPerSec { get; private set; }
        internal float QuaternionDeltaLocalZRateDegPerSec { get; private set; }
        internal float QuaternionDeltaAngleDeg { get; private set; }
        internal float QuaternionDeltaConfidence { get; private set; }

        // v0.4.14 formal attitude-rate mapping, established by the v0.4.11 manual-axis test:
        // local X = pitch, local Y = roll, local Z = yaw. These remain observe-only.
        internal bool EstimatorValid { get; private set; }
        internal float EstimatorBankDeg { get; private set; }
        internal float EstimatorBankWrappedDeg { get; private set; }
        internal float EstimatorBankDeltaDeg { get; private set; }

        // v0.4.27: instantaneous local-horizon bank for BANK control.
        // Recomputed from gravity and active control frame; right-wing-down is positive.
        internal float HorizonBankDeg { get; private set; }
        internal bool HorizonBankValid { get; private set; }
        internal float HorizonBankConfidence { get; private set; }
        internal float EstimatorRollRateDegPerSec { get; private set; }
        internal float EstimatorPitchRateDegPerSec { get; private set; }
        internal float EstimatorYawRateDegPerSec { get; private set; }
        internal float EstimatorConfidence { get; private set; }

        // Legacy roll-named telemetry remains published for comparison, sourced from the
        // local X component exactly as in v0.4.10. It is not claimed to be RollRate.
        internal bool QuaternionRollValid { get; private set; }
        internal float QuaternionRollRateDegPerSec { get; private set; }
        internal float QuaternionRollDeltaDeg { get; private set; }
        internal float QuaternionRollIntegratedDeg { get; private set; }
        internal float QuaternionRollWrappedDeg { get; private set; }
        internal float QuaternionRollConfidence { get; private set; }

        bool havePreviousControlBankWrapped;
        float previousControlBankWrappedDeg;
        bool haveGyroIntegrationTime;
        float previousGyroIntegrationTime;
        bool havePreviousControlRotation;
        Quaternion previousControlRotation;
        float previousQuaternionRollTime;
        string trackerVesselId;
        // Persistent finite-difference state for crosscheck-only angular acceleration.
        bool havePreviousInstrumentRates;
        float previousInstrumentRollRateDegPerSec;
        float previousInstrumentPitchRateDegPerSec;
        float previousInstrumentYawRateDegPerSec;
        float previousInstrumentRateFixedTime;
        internal string GyroIntegrationResetReason { get; private set; } = "startup";
        internal string Source { get { return "KSP vanilla truth / virtual sensors + control-point quaternion-delta 3-axis calibration"; } }
        // v0.4.27 formal public instrument outputs. BANK is estimator-derived; pitch and heading use the
        // same KSP control-frame quaternion source used by the recorder. No control authority is granted.
        internal float InstrumentBankDeg { get { return EstimatorBankDeg; } }
        internal float InstrumentBankWrappedDeg { get { return EstimatorBankWrappedDeg; } }
        internal float InstrumentHorizonBankDeg { get { return HorizonBankDeg; } }
        internal bool InstrumentHorizonBankValid { get { return HorizonBankValid; } }
        internal float InstrumentHorizonBankConfidence { get { return HorizonBankConfidence; } }
        internal float InstrumentPitchDeg { get { return PitchDeg; } }
        internal float InstrumentHeadingDeg { get { return HeadingDeg; } }
        internal float InstrumentRollRateDegPerSec { get { return EstimatorRollRateDegPerSec; } }
        internal float InstrumentPitchRateDegPerSec { get { return EstimatorPitchRateDegPerSec; } }
        internal float InstrumentYawRateDegPerSec { get { return EstimatorYawRateDegPerSec; } }
        internal bool InstrumentValid { get { return EstimatorValid && Valid && PitchValid; } }
        internal bool InstrumentPitchValid { get { return PitchValid; } }
        internal bool InstrumentHeadingValid { get { return HeadingValid; } }
        internal float InstrumentVanillaNavballHeadingDeg { get { return VanillaNavballHeadingDeg; } }
        internal bool InstrumentVanillaNavballHeadingValid { get { return VanillaNavballHeadingValid; } }
        internal float InstrumentHeadingErrorDeg { get { return HeadingErrorDeg; } }
        internal string InstrumentHeadingStatus { get { return HeadingStatus ?? "UNAVAILABLE"; } }
        internal float InstrumentConfidence { get { return EstimatorConfidence; } }
        // Visual Navball alignment is a UI verification state, never an input to control.
        internal string NavballAlignmentState { get { return !InstrumentValid ? "NO ATTITUDE" : (!HeadingValid ? HeadingStatus : "VERIFY WITH KSP NAVBALL"); } }

        internal void Update(Vessel vessel)
        {
            Reset();
            if (vessel == null || vessel.packed || vessel.mainBody == null || vessel.transform == null) return;

            string currentVesselId = vessel.id.ToString();
            if (trackerVesselId != currentVesselId)
            {
                trackerVesselId = currentVesselId;
                havePreviousControlBankWrapped = false;
                ControlBankUnwrappedDeg = 0f;
                GyroIntegratedBankDeg = 0f;
                QuaternionRollIntegratedDeg = 0f;
                EstimatorBankDeg = 0f;
                haveGyroIntegrationTime = false;
                havePreviousControlRotation = false;
                previousQuaternionRollTime = 0f;
                havePreviousInstrumentRates = false;
                previousInstrumentRateFixedTime = 0f;
                LastSampleFixedTime = -1f;
                GyroIntegrationResetReason = "vessel-change";
            }

            Vector3 position = vessel.rootPart != null ? vessel.rootPart.transform.position : vessel.transform.position;
            Vector3 radial = position - vessel.mainBody.position;
            if (!IsFinite(position) || !IsFinite(radial) || radial.sqrMagnitude < 0.0001f) return;
            GravityUp = radial.normalized;

            Transform t = vessel.transform;
            RawQuaternion = t.rotation;
            if (!IsFinite(RawQuaternion) || !IsFinite(t.forward) || !IsFinite(t.up) || !IsFinite(t.right)) return;
            // Raw Euler values are not a usable flight attitude reference. Formal pitch and
            // heading are derived later from the active KSP control frame and local gravity.
            Vector3 euler = RawQuaternion.eulerAngles;
            BankDeg = NormalizeSigned(euler.z);
            PitchDeg = 0f;
            HeadingDeg = 0f;
            PitchValid = false;
            HeadingValid = false;
            HeadingStatus = "UNAVAILABLE";

            Vector3d omega = vessel.angularVelocity;
            float nativeRollRate = (float)Vector3d.Dot(omega, (Vector3d)t.up) * Mathf.Rad2Deg;
            float nativePitchRate = (float)Vector3d.Dot(omega, (Vector3d)t.right) * Mathf.Rad2Deg;
            float nativeYawRate = (float)Vector3d.Dot(omega, (Vector3d)t.forward) * Mathf.Rad2Deg;
            RollRateDegPerSec = IsFinite(nativeRollRate) ? nativeRollRate : 0f;
            PitchRateDegPerSec = IsFinite(nativePitchRate) ? nativePitchRate : 0f;
            YawRateDegPerSec = IsFinite(nativeYawRate) ? nativeYawRate : 0f;

            // Formal shared-native baseline. AA's corresponding values remain independently
            // measured in comparison telemetry; no AA FlightModel value is read or written here.
            float surfaceSpeedSample = (float)vessel.srfSpeed;
            SharedSurfaceSpeedValid = IsFinite(surfaceSpeedSample) && surfaceSpeedSample >= 0f;
            SurfaceSpeedMps = SharedSurfaceSpeedValid ? surfaceSpeedSample : 0f;

            float altitudeAslSample = (float)vessel.altitude;
            AltitudeAslValid = IsFinite(altitudeAslSample);
            AltitudeAslM = AltitudeAslValid ? Mathf.Max(0f, altitudeAslSample) : 0f;

            float radarAltitudeSample = (float)vessel.heightFromTerrain;
            SharedRadarAltitudeValid = IsFinite(radarAltitudeSample);
            RadarAltitudeM = SharedRadarAltitudeValid ? Mathf.Max(0f, radarAltitudeSample) : 0f;

            Vector3 surfaceVelocitySample = (Vector3)vessel.srf_velocity;
            bool surfaceVelocityValid = IsFinite(surfaceVelocitySample);
            Vector3 surfaceVelocity = surfaceVelocityValid ? surfaceVelocitySample : Vector3.zero;
            SurfaceVelocityDirection = surfaceVelocityValid && surfaceVelocity.sqrMagnitude > 0.0001f
                ? surfaceVelocity.normalized : Vector3.zero;
            float verticalSpeedSample = surfaceVelocityValid ? Vector3.Dot(surfaceVelocity, GravityUp) : 0f;
            VerticalSpeedValid = surfaceVelocityValid && IsFinite(verticalSpeedSample);
            VerticalSpeedMps = VerticalSpeedValid ? verticalSpeedSample : 0f;

            float staticPressureSample = (float)vessel.staticPressurekPa;
            StaticPressureKpa = IsFinite(staticPressureSample) ? Mathf.Max(0f, staticPressureSample) : 0f;
            float densitySample = (float)vessel.atmDensity;
            bool densityValid = IsFinite(densitySample) && densitySample >= 0f;
            DensityKgM3 = densityValid ? densitySample : 0f;
            float dynamicPressureSample = 0.5f * DensityKgM3 * SurfaceSpeedMps * SurfaceSpeedMps / 1000f;
            SharedDynamicPressureValid = SharedSurfaceSpeedValid && densityValid &&
                IsFinite(dynamicPressureSample) && dynamicPressureSample >= 0f;
            DynamicPressureKpa = SharedDynamicPressureValid ? dynamicPressureSample : 0f;
            AltitudeAslSharedBaselineValid = false;
            AltitudeAslSource = "AERIS_KSP_NATIVE_UNSHARED";
            CommonKinematicBaselineValid = SharedSurfaceSpeedValid && SharedRadarAltitudeValid && SharedDynamicPressureValid;
            CommonKinematicBaselineSource = CommonKinematicBaselineValid
                ? "KSP_SHARED_NATIVE_SPEED_RADAR_Q" : "KSP_SHARED_NATIVE_INVALID";
            float geeForceSample = (float)vessel.geeForce;
            GeeForce = IsFinite(geeForceSample) ? geeForceSample : 0f;

            TransformForwardGravityDot = Vector3.Dot(t.forward, GravityUp);
            TransformUpGravityDot = Vector3.Dot(t.up, GravityUp);
            TransformRightGravityDot = Vector3.Dot(t.right, GravityUp);

            // A systematic calibration set: measure gravity-frame roll about each possible body axis.
            // At this point these are observations, not a claimed Navball solution.
            float bankForward;
            float bankUp;
            float bankRight;
            bool a = TryGravityRoll(t.forward, t.up, t.right, out bankForward);
            bool b = TryGravityRoll(t.up, t.right, t.forward, out bankUp);
            bool c = TryGravityRoll(t.right, t.up, t.forward, out bankRight);
            BankAroundForwardDeg = bankForward;
            BankAroundUpDeg = bankUp;
            BankAroundRightDeg = bankRight;
            GravityFrameValid = a || b || c;
            DerivedBankConfidence = GravityFrameValid ? 1.0f : 0.0f;

            // Explicit AERIS body frame. This is a named, stable convention so every future
            // sensor, AP mode, and add-on can compare the same axes during calibration.
            Vector3 bodyLongitudinal = t.up.normalized;
            Vector3 bodyLateral = t.right.normalized;
            Vector3 bodyVertical = (-t.forward).normalized;
            BodyLongitudinalGravityDot = Vector3.Dot(bodyLongitudinal, GravityUp);
            BodyLateralGravityDot = Vector3.Dot(bodyLateral, GravityUp);
            BodyVerticalGravityDot = Vector3.Dot(bodyVertical, GravityUp);
            Vector3 levelVertical = Vector3.ProjectOnPlane(GravityUp, bodyLongitudinal);
            Vector3 levelLateral = Vector3.Cross(levelVertical, bodyLongitudinal);
            BodyFrameValid = levelVertical.sqrMagnitude > 0.0001f && levelLateral.sqrMagnitude > 0.0001f;
            if (BodyFrameValid)
            {
                levelVertical.Normalize();
                levelLateral.Normalize();
                // Legacy signed-angle candidate retained for comparison.
                BodyFrameBankDeg = -Vector3.SignedAngle(levelVertical, bodyVertical, bodyLongitudinal);
                BodyFramePitchDeg = Mathf.Asin(Mathf.Clamp(BodyLongitudinalGravityDot, -1f, 1f)) * Mathf.Rad2Deg;

                // Direct projection estimator. For the declared body frame:
                // level flight => vertical·gUp=+1 and lateral·gUp=0.
                // Right-wing-down gives lateral·gUp < 0, so positive bank is -atan2.
                GravityProjectionBankDeg = -Mathf.Atan2(BodyLateralGravityDot, BodyVerticalGravityDot) * Mathf.Rad2Deg;
                GravityProjectionPitchDeg = Mathf.Asin(Mathf.Clamp(BodyLongitudinalGravityDot, -1f, 1f)) * Mathf.Rad2Deg;
                GravityProjectionValid = !float.IsNaN(GravityProjectionBankDeg) && !float.IsInfinity(GravityProjectionBankDeg)
                    && !float.IsNaN(GravityProjectionPitchDeg) && !float.IsInfinity(GravityProjectionPitchDeg);
                GravityProjectionConfidence = GravityProjectionValid && Mathf.Abs(BodyLongitudinalGravityDot) < 0.98f ? 1.0f : (GravityProjectionValid ? 0.25f : 0f);
                Vector3 north = Vector3.ProjectOnPlane(Vector3.forward, GravityUp);
                Vector3 forwardHorizontal = Vector3.ProjectOnPlane(bodyLongitudinal, GravityUp);
                if (north.sqrMagnitude > 0.0001f && forwardHorizontal.sqrMagnitude > 0.0001f)
                {
                    north.Normalize(); forwardHorizontal.Normalize();
                    BodyFrameHeadingDeg = Mathf.Repeat(Vector3.SignedAngle(north, forwardHorizontal, GravityUp), 360f);
                }
                else BodyFrameHeadingDeg = 0f;
                BodyFrameConfidence = Mathf.Abs(BodyLongitudinalGravityDot) < 0.98f ? 1.0f : 0.25f;
            }
            else
            {
                BodyFrameBankDeg = BodyFramePitchDeg = BodyFrameHeadingDeg = 0f;
                BodyFrameConfidence = 0f;
                GravityProjectionBankDeg = GravityProjectionPitchDeg = 0f;
                GravityProjectionValid = false;
                GravityProjectionConfidence = 0f;
            }

            // KSP active control/reference frame: record raw axes and frame deltas, no axis remapping.
            Transform control = vessel.ReferenceTransform;
            if (control != null)
            {
                ControlFrameName = control.name ?? "ReferenceTransform";
                ControlForwardGravityDot = Vector3.Dot(control.forward, GravityUp);
                ControlUpGravityDot = Vector3.Dot(control.up, GravityUp);
                ControlRightGravityDot = Vector3.Dot(control.right, GravityUp);
                ControlVsVesselRotationDeg = Quaternion.Angle(control.rotation, vessel.transform.rotation);
                Transform root = vessel.rootPart != null ? vessel.rootPart.transform : null;
                ControlVsRootRotationDeg = root != null ? Quaternion.Angle(control.rotation, root.rotation) : 0f;
                float cf, cu, cr;
                bool cfa = TryGravityRoll(control.forward, control.up, control.right, out cf);
                bool cub = TryGravityRoll(control.up, control.right, control.forward, out cu);
                bool crc = TryGravityRoll(control.right, control.up, control.forward, out cr);
                ControlBankAboutForwardDeg = cf;
                ControlBankAboutUpDeg = cu;
                ControlBankAboutRightDeg = cr;
                ControlFrameValid = cfa || cub || crc;
                ControlFrameConfidence = ControlFrameValid ? 1f : 0f;

                // Formal geometric pitch / heading. In KSP's active control frame the
                // longitudinal / nose axis is control.up. This avoids Euler-gimbal coupling:
                // pitch is the angle of the nose above the local horizon, while heading is
                // its local-horizontal bearing. Near vertical, heading is mathematically
                // undefined and is intentionally marked invalid instead of emitting a lie.
                Vector3 longitudinal = control.up.normalized;
                float longitudinalUp = Vector3.Dot(longitudinal, GravityUp);
                PitchDeg = Mathf.Asin(Mathf.Clamp(longitudinalUp, -1f, 1f)) * Mathf.Rad2Deg;
                PitchValid = !float.IsNaN(PitchDeg) && !float.IsInfinity(PitchDeg);

                // Heading is a local-horizontal bearing, not a quaternion yaw/Euler value.
                // Build local north by projecting the body's *rotation axis* (global north) onto
                // the local tangent plane. GetSurfaceNVector is the local radial/up vector and
                // projects to zero here, which made earlier heading validation report invalid everywhere.
                Vector3 forwardHorizontal = Vector3.ProjectOnPlane(longitudinal, GravityUp);
                Vector3d upD = (vessel.CoM - vessel.mainBody.position).normalized;
                // Local north is the body rotation axis projected into the local tangent plane.
                // This works on any rotating body regardless of its axial tilt. Near either pole,
                // the projection collapses and north/heading are intentionally undefined.
                Vector3d northD = Vector3d.Exclude(upD, vessel.mainBody.RotationAxis);
                Vector3 localNorth = (Vector3)northD;
                bool noseHasHorizontal = forwardHorizontal.sqrMagnitude > 0.0004f;
                bool northHasHorizontal = localNorth.sqrMagnitude > 0.0004f;
                if (noseHasHorizontal && northHasHorizontal)
                {
                    forwardHorizontal.Normalize();
                    localNorth.Normalize();
                    HeadingDeg = Mathf.Repeat(Vector3.SignedAngle(localNorth, forwardHorizontal, GravityUp), 360f);
                    HeadingValid = !float.IsNaN(HeadingDeg) && !float.IsInfinity(HeadingDeg);
                    HeadingStatus = HeadingValid ? "VALID" : "HEADING INVALID";
                }
                else
                {
                    HeadingDeg = 0f;
                    HeadingValid = false;
                    HeadingStatus = !northHasHorizontal ? "HEADING UNDEFINED NEAR POLE" : "HEADING UNDEFINED NEAR VERTICAL";
                }

                // KSP does not expose a Vessel.heading member in 1.12.5. Read FlightGlobals.ship_heading
                // reflectively so this comparison path remains optional and does not bind AERIS to a
                // non-portable member at compile time. When unavailable, the reference remains invalid.
                float vanillaHeading;
                VanillaNavballHeadingValid = TryReadVanillaNavballHeading(out vanillaHeading);
                VanillaNavballHeadingDeg = VanillaNavballHeadingValid ? Mathf.Repeat(vanillaHeading, 360f) : 0f;
                HeadingErrorDeg = (HeadingValid && VanillaNavballHeadingValid)
                    ? Mathf.DeltaAngle(VanillaNavballHeadingDeg, HeadingDeg) : 0f;

                // Direct local-horizon bank using the active KSP control frame.
                // control.up = nose/longitudinal; control.right = right wing; -control.forward = top.
                // Right wing down makes right·gravityUp negative, yielding positive BANK below.
                float controlRightUp = Vector3.Dot(control.right.normalized, GravityUp);
                float controlTopUp = Vector3.Dot((-control.forward).normalized, GravityUp);
                float longitudinalHorizontal = Vector3.ProjectOnPlane(longitudinal, GravityUp).magnitude;
                HorizonBankValid = longitudinalHorizontal > 0.02f;
                HorizonBankDeg = HorizonBankValid
                    ? NormalizeSigned(-Mathf.Atan2(controlRightUp, controlTopUp) * Mathf.Rad2Deg)
                    : 0f;
                HorizonBankValid = HorizonBankValid && !float.IsNaN(HorizonBankDeg) && !float.IsInfinity(HorizonBankDeg);
                HorizonBankConfidence = HorizonBankValid ? Mathf.Clamp01(longitudinalHorizontal) : 0f;

                // Candidate sign convention: right bank positive. The existing
                // right-axis trace was observed inverted during v0.4.4; preserve it
                // separately above and publish this normalized candidate for testing.
                ControlBankCandidateValid = crc && !float.IsNaN(cr) && !float.IsInfinity(cr);
                ControlBankCandidateDeg = ControlBankCandidateValid ? NormalizeSigned(-cr) : 0f;
                ControlBankCandidateConfidence = ControlBankCandidateValid ? ControlFrameConfidence : 0f;

                ControlBankWrappedDeg = ControlBankCandidateDeg;
                if (ControlBankCandidateValid)
                {
                    if (!havePreviousControlBankWrapped)
                    {
                        previousControlBankWrappedDeg = ControlBankWrappedDeg;
                        ControlBankUnwrappedDeg = ControlBankWrappedDeg;
                        ControlBankUnwrappedDeltaDeg = 0f;
                        havePreviousControlBankWrapped = true;
                    }
                    else
                    {
                        float delta = Mathf.DeltaAngle(previousControlBankWrappedDeg, ControlBankWrappedDeg);
                        ControlBankUnwrappedDeg += delta;
                        ControlBankUnwrappedDeltaDeg = delta;
                        previousControlBankWrappedDeg = ControlBankWrappedDeg;
                    }
                    ControlBankUnwrappedValid = true;
                    ControlBankUnwrappedConfidence = ControlBankCandidateConfidence;
                }
                else
                {
                    ControlBankUnwrappedValid = false;
                    ControlBankUnwrappedConfidence = 0f;
                    havePreviousControlBankWrapped = false;
                }

                // Raw gyro sample in the active control-point frame. This is the sensor
                // source for the upcoming RollRate choice, not a control input.
                Vector3d controlOmega = vessel.angularVelocity;
                ControlGyroForwardRateDegPerSec = (float)Vector3d.Dot(controlOmega, (Vector3d)control.forward) * Mathf.Rad2Deg;
                ControlGyroUpRateDegPerSec = (float)Vector3d.Dot(controlOmega, (Vector3d)control.up) * Mathf.Rad2Deg;
                ControlGyroRightRateDegPerSec = (float)Vector3d.Dot(controlOmega, (Vector3d)control.right) * Mathf.Rad2Deg;
                ControlGyroMagnitudeDegPerSec = (float)controlOmega.magnitude * Mathf.Rad2Deg;
                ControlGyroValid = !float.IsNaN(ControlGyroForwardRateDegPerSec) && !float.IsInfinity(ControlGyroForwardRateDegPerSec)
                    && !float.IsNaN(ControlGyroUpRateDegPerSec) && !float.IsInfinity(ControlGyroUpRateDegPerSec)
                    && !float.IsNaN(ControlGyroRightRateDegPerSec) && !float.IsInfinity(ControlGyroRightRateDegPerSec);
                // v0.4.13b: world angular-velocity projections remain diagnostic only.
                // The tracker uses a local Quaternion delta, so the body-right axis is
                // represented consistently through a full roll.
                float now = Time.fixedTime;
                GyroIntegratedBankDeltaDeg = 0f;
                QuaternionRollDeltaDeg = 0f;

                if (!havePreviousControlRotation)
                {
                    previousControlRotation = control.rotation;
                    previousQuaternionRollTime = now;
                    havePreviousControlRotation = true;
                    QuaternionDeltaValid = true;
                    QuaternionDeltaLocalXDeg = QuaternionDeltaLocalYDeg = QuaternionDeltaLocalZDeg = 0f;
                    QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalZRateDegPerSec = 0f;
                    QuaternionDeltaAngleDeg = 0f;
                    QuaternionDeltaConfidence = 1f;
                    QuaternionRollValid = true;
                    QuaternionRollConfidence = 1f;
                    EstimatorValid = true;
                    EstimatorBankDeltaDeg = 0f;
                    EstimatorBankWrappedDeg = NormalizeWrapped(EstimatorBankDeg);
                    EstimatorRollRateDegPerSec = EstimatorPitchRateDegPerSec = EstimatorYawRateDegPerSec = 0f;
                    EstimatorConfidence = 1f;
                    GyroIntegrationResetReason = "quaternion-prime";
                }
                else
                {
                    float dt = now - previousQuaternionRollTime;
                    if (dt > 0f && dt <= 0.25f)
                    {
                        // Relative rotation expressed in the previous control/body frame.
                        Quaternion dq = Quaternion.Inverse(previousControlRotation) * control.rotation;
                        // q and -q represent the same orientation. Keep the shortest increment.
                        if (dq.w < 0f) dq = new Quaternion(-dq.x, -dq.y, -dq.z, -dq.w);
                        float sinHalf = Mathf.Sqrt(dq.x*dq.x + dq.y*dq.y + dq.z*dq.z);
                        if (sinHalf > 0.000001f)
                        {
                            float angleDeg = 2f * Mathf.Atan2(sinHalf, Mathf.Clamp(dq.w, -1f, 1f)) * Mathf.Rad2Deg;
                            Vector3 localAxis = new Vector3(dq.x, dq.y, dq.z) / sinHalf;
                            // Keep all components. Unity Quaternion local x/y/z are merely raw
                            // control-frame components here; this calibration must establish which one
                            // corresponds to pilot roll, pitch, and yaw on the active KSP craft frame.
                            QuaternionDeltaLocalXDeg = localAxis.x * angleDeg;
                            QuaternionDeltaLocalYDeg = localAxis.y * angleDeg;
                            QuaternionDeltaLocalZDeg = localAxis.z * angleDeg;
                            QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalXDeg / dt;
                            QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalYDeg / dt;
                            QuaternionDeltaLocalZRateDegPerSec = QuaternionDeltaLocalZDeg / dt;
                            QuaternionDeltaAngleDeg = angleDeg;
                            QuaternionDeltaValid = true;
                            QuaternionDeltaConfidence = 1f;

                            // Formal v0.4.14 player-visible convention.
                            // Right roll/bank is positive; left roll/bank is negative.
                            // The measured local-Y quaternion delta has the opposite sign on this KSP control frame.
                            EstimatorBankDeltaDeg = -QuaternionDeltaLocalYDeg;
                            EstimatorBankDeg += EstimatorBankDeltaDeg;
                            EstimatorBankWrappedDeg = NormalizeWrapped(EstimatorBankDeg);
                            EstimatorRollRateDegPerSec = -QuaternionDeltaLocalYRateDegPerSec;
                            EstimatorPitchRateDegPerSec = -QuaternionDeltaLocalXRateDegPerSec;
                            EstimatorYawRateDegPerSec = -QuaternionDeltaLocalZRateDegPerSec;
                            EstimatorValid = true;
                            EstimatorConfidence = 1f;
                            RollRateDegPerSec = EstimatorRollRateDegPerSec;
                            PitchRateDegPerSec = EstimatorPitchRateDegPerSec;
                            YawRateDegPerSec = EstimatorYawRateDegPerSec;

                            // Legacy v0.4.10 comparison trace. It used -localAxis.x.
                            QuaternionRollDeltaDeg = -QuaternionDeltaLocalXDeg;
                            QuaternionRollIntegratedDeg += QuaternionRollDeltaDeg;
                            QuaternionRollRateDegPerSec = QuaternionRollDeltaDeg / dt;
                        }
                        else
                        {
                            QuaternionDeltaLocalXDeg = QuaternionDeltaLocalYDeg = QuaternionDeltaLocalZDeg = 0f;
                            QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalZRateDegPerSec = 0f;
                            QuaternionDeltaAngleDeg = 0f;
                            QuaternionDeltaValid = true;
                            QuaternionDeltaConfidence = 1f;
                            QuaternionRollDeltaDeg = 0f;
                            QuaternionRollRateDegPerSec = 0f;
                            EstimatorBankDeltaDeg = 0f;
                            EstimatorBankWrappedDeg = NormalizeWrapped(EstimatorBankDeg);
                            EstimatorRollRateDegPerSec = EstimatorPitchRateDegPerSec = EstimatorYawRateDegPerSec = 0f;
                            EstimatorValid = true;
                            EstimatorConfidence = 1f;
                            RollRateDegPerSec = 0f;
                            PitchRateDegPerSec = 0f;
                            YawRateDegPerSec = 0f;
                        }

                        QuaternionRollWrappedDeg = NormalizeWrapped(QuaternionRollIntegratedDeg);
                        QuaternionRollValid = true;
                        QuaternionRollConfidence = 1f;
                        // Retain old telemetry names for continuity, now sourced from quaternion delta.
                        GyroIntegratedBankDeltaDeg = QuaternionRollDeltaDeg;
                        GyroIntegratedBankDeg = QuaternionRollIntegratedDeg;
                        GyroIntegratedBankWrappedDeg = QuaternionRollWrappedDeg;
                        GyroIntegratedBankValid = true;
                        GyroIntegratedBankConfidence = 1f;
                        RollRateDegPerSec = QuaternionRollRateDegPerSec;
                    }
                    else
                    {
                        QuaternionDeltaValid = false;
                        QuaternionDeltaLocalXDeg = QuaternionDeltaLocalYDeg = QuaternionDeltaLocalZDeg = 0f;
                        QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalZRateDegPerSec = 0f;
                        QuaternionDeltaAngleDeg = 0f;
                        QuaternionDeltaConfidence = 0f;
                        QuaternionRollRateDegPerSec = 0f;
                        QuaternionRollValid = false;
                        QuaternionRollConfidence = 0f;
                        EstimatorValid = false;
                        EstimatorBankDeltaDeg = 0f;
                        EstimatorRollRateDegPerSec = EstimatorPitchRateDegPerSec = EstimatorYawRateDegPerSec = 0f;
                        EstimatorConfidence = 0f;
                        GyroIntegrationResetReason = "quaternion-gap";
                    }
                }

                previousControlRotation = control.rotation;
                previousQuaternionRollTime = now;
                if (ControlGyroValid && !QuaternionRollValid)
                    RollRateDegPerSec = -ControlGyroRightRateDegPerSec;
            }
            else
            {
                havePreviousControlBankWrapped = false;
                ControlBankUnwrappedValid = false;
                ControlBankUnwrappedConfidence = 0f;
                ControlGyroValid = false;
                ControlGyroForwardRateDegPerSec = ControlGyroUpRateDegPerSec = ControlGyroRightRateDegPerSec = ControlGyroMagnitudeDegPerSec = 0f;
                GyroIntegratedBankValid = false; GyroIntegratedBankConfidence = 0f; GyroIntegratedBankDeltaDeg = 0f; haveGyroIntegrationTime = false;
                QuaternionDeltaValid = false; QuaternionDeltaLocalXDeg = QuaternionDeltaLocalYDeg = QuaternionDeltaLocalZDeg = 0f; QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalZRateDegPerSec = 0f; QuaternionDeltaAngleDeg = QuaternionDeltaConfidence = 0f;
                QuaternionRollValid = false; QuaternionRollRateDegPerSec = QuaternionRollDeltaDeg = 0f; QuaternionRollConfidence = 0f; havePreviousControlRotation = false;
                EstimatorValid = false; EstimatorBankDeltaDeg = 0f; EstimatorRollRateDegPerSec = EstimatorPitchRateDegPerSec = EstimatorYawRateDegPerSec = 0f; EstimatorConfidence = 0f;
                GyroIntegrationResetReason = "control-frame-missing";
            }

            // Crosscheck-only independent geometric AoA estimate. AA uses a smoothed virtualRotation;
            // this uses the AERIS/KSP active control frame directly so any divergence is meaningful.
            UpdateEstimatedAoA(vessel.ReferenceTransform, surfaceVelocity);
            UpdateInstrumentAngularAcceleration(Time.fixedTime);
            LastSampleFixedTime = Time.fixedTime;

            Confidence = SurfaceSpeedMps < 1f ? 0.70f : SurfaceSpeedMps < 10f ? 0.85f : 1.0f;
            Valid = true;
        }

        // Observation only. This intentionally mirrors AA's geometric AoA decomposition while
        // using the unsmoothed AERIS active control frame and vessel surface velocity.
        void UpdateEstimatedAoA(Transform control, Vector3 surfaceVelocity)
        {
            EstimatedAoAValid = false;
            EstimatedPitchAoADeg = EstimatedRollAoADeg = EstimatedYawAoADeg = 0f;
            if (control == null || surfaceVelocity.sqrMagnitude <= 1.0f) return;

            Vector3 up = control.rotation * Vector3.up;
            Vector3 forward = control.rotation * Vector3.forward;
            Vector3 right = control.rotation * Vector3.right;
            Vector3 upProjected = Vector3.Project(surfaceVelocity, up);
            Vector3 forwardProjected = Vector3.Project(surfaceVelocity, forward);
            Vector3 rightProjected = Vector3.Project(surfaceVelocity, right);

            Vector3 pitchPlane = upProjected + forwardProjected;
            Vector3 yawPlane = upProjected + rightProjected;
            Vector3 rollPlane = rightProjected + forwardProjected;
            if (pitchPlane.sqrMagnitude <= 1.0f || yawPlane.sqrMagnitude <= 1.0f || rollPlane.sqrMagnitude <= 1.0f) return;

            float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, pitchPlane.normalized), -1f, 1f));
            if (Vector3.Dot(pitchPlane, up) < 0f) pitch = Mathf.PI - pitch;
            float yaw = Mathf.Asin(Mathf.Clamp(Vector3.Dot(-right, yawPlane.normalized), -1f, 1f));
            if (Vector3.Dot(yawPlane, up) < 0f) yaw = Mathf.PI - yaw;
            float roll = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, rollPlane.normalized), -1f, 1f));
            if (Vector3.Dot(rollPlane, right) < 0f) roll = Mathf.PI - roll;

            EstimatedPitchAoADeg = pitch * Mathf.Rad2Deg;
            EstimatedRollAoADeg = roll * Mathf.Rad2Deg;
            EstimatedYawAoADeg = yaw * Mathf.Rad2Deg;
            EstimatedAoAValid = !float.IsNaN(EstimatedPitchAoADeg) && !float.IsInfinity(EstimatedPitchAoADeg)
                && !float.IsNaN(EstimatedRollAoADeg) && !float.IsInfinity(EstimatedRollAoADeg)
                && !float.IsNaN(EstimatedYawAoADeg) && !float.IsInfinity(EstimatedYawAoADeg);
        }

        // Finite-difference of the already formalized AERIS instrument rates.  It is not a
        // control signal and has no consumer outside the comparison recorder in this phase.
        void UpdateInstrumentAngularAcceleration(float nowFixedTime)
        {
            InstrumentAngularAccelerationValid = false;
            InstrumentRollAccelerationDegPerSec2 = 0f;
            InstrumentPitchAccelerationDegPerSec2 = 0f;
            InstrumentYawAccelerationDegPerSec2 = 0f;
            if (!EstimatorValid || !PitchValid)
            {
                havePreviousInstrumentRates = false;
                return;
            }

            if (havePreviousInstrumentRates)
            {
                float dt = nowFixedTime - previousInstrumentRateFixedTime;
                if (dt > 0.0005f && dt < 0.25f)
                {
                    InstrumentRollAccelerationDegPerSec2 = (EstimatorRollRateDegPerSec - previousInstrumentRollRateDegPerSec) / dt;
                    InstrumentPitchAccelerationDegPerSec2 = (EstimatorPitchRateDegPerSec - previousInstrumentPitchRateDegPerSec) / dt;
                    InstrumentYawAccelerationDegPerSec2 = (EstimatorYawRateDegPerSec - previousInstrumentYawRateDegPerSec) / dt;
                    InstrumentAngularAccelerationValid = true;
                }
            }

            previousInstrumentRollRateDegPerSec = EstimatorRollRateDegPerSec;
            previousInstrumentPitchRateDegPerSec = EstimatorPitchRateDegPerSec;
            previousInstrumentYawRateDegPerSec = EstimatorYawRateDegPerSec;
            previousInstrumentRateFixedTime = nowFixedTime;
            havePreviousInstrumentRates = true;
        }

        bool TryGravityRoll(Vector3 longitudinalAxis, Vector3 nominalUpAxis, Vector3 nominalRightAxis, out float result)
        {
            result = 0f;
            if (longitudinalAxis.sqrMagnitude < 0.0001f) return false;
            Vector3 axis = longitudinalAxis.normalized;
            Vector3 levelUp = Vector3.ProjectOnPlane(GravityUp, axis);
            Vector3 bodyUp = Vector3.ProjectOnPlane(nominalUpAxis, axis);
            if (levelUp.sqrMagnitude < 0.0001f || bodyUp.sqrMagnitude < 0.0001f) return false;
            levelUp.Normalize(); bodyUp.Normalize();
            // Sign follows the supplied right-axis convention, making the handedness explicit in FDR.
            result = Vector3.SignedAngle(levelUp, bodyUp, axis);
            return !float.IsNaN(result) && !float.IsInfinity(result);
        }

        void Reset()
        {
            Valid = false; Confidence = 0f; BankDeg = PitchDeg = HeadingDeg = 0f;
            PitchValid = false; HeadingValid = false; VanillaNavballHeadingValid = false; VanillaNavballHeadingDeg = HeadingErrorDeg = 0f; HeadingStatus = "UNAVAILABLE";
            RollRateDegPerSec = PitchRateDegPerSec = YawRateDegPerSec = 0f;
            EstimatedAoAValid = false; EstimatedPitchAoADeg = EstimatedRollAoADeg = EstimatedYawAoADeg = 0f;
            InstrumentAngularAccelerationValid = false;
            InstrumentRollAccelerationDegPerSec2 = InstrumentPitchAccelerationDegPerSec2 = InstrumentYawAccelerationDegPerSec2 = 0f;
            CommonKinematicBaselineValid = false;
            CommonKinematicBaselineSource = "UNAVAILABLE";
            SharedSurfaceSpeedValid = false;
            SharedRadarAltitudeValid = false;
            SharedDynamicPressureValid = false;
            AltitudeAslValid = false;
            VerticalSpeedValid = false;
            AltitudeAslSharedBaselineValid = false;
            AltitudeAslSource = "UNAVAILABLE";
            SurfaceSpeedMps = AltitudeAslM = RadarAltitudeM = VerticalSpeedMps = DynamicPressureKpa = StaticPressureKpa = DensityKgM3 = GeeForce = 0f;
            GravityUp = Vector3.zero; SurfaceVelocityDirection = Vector3.zero;
            GravityFrameValid = false; RawQuaternion = Quaternion.identity;
            TransformForwardGravityDot = TransformUpGravityDot = TransformRightGravityDot = 0f;
            BankAroundForwardDeg = BankAroundUpDeg = BankAroundRightDeg = 0f;
            DerivedBankConfidence = 0f;
            BodyFrameValid = false;
            BodyLongitudinalGravityDot = BodyLateralGravityDot = BodyVerticalGravityDot = 0f;
            BodyFrameBankDeg = BodyFramePitchDeg = BodyFrameHeadingDeg = BodyFrameConfidence = 0f;
            GravityProjectionBankDeg = GravityProjectionPitchDeg = GravityProjectionConfidence = 0f;
            GravityProjectionValid = false;
            ControlFrameValid = false; ControlFrameName = "none";
            ControlForwardGravityDot = ControlUpGravityDot = ControlRightGravityDot = 0f;
            ControlVsVesselRotationDeg = ControlVsRootRotationDeg = 0f;
            ControlBankAboutForwardDeg = ControlBankAboutUpDeg = ControlBankAboutRightDeg = 0f;
            ControlFrameConfidence = 0f;
            ControlBankCandidateValid = false;
            ControlBankCandidateDeg = ControlBankCandidateConfidence = 0f;
            ControlBankUnwrappedValid = false;
            ControlBankWrappedDeg = ControlBankUnwrappedDeltaDeg = 0f;
            ControlBankUnwrappedConfidence = 0f;
            ControlGyroValid = false;
            ControlGyroForwardRateDegPerSec = ControlGyroUpRateDegPerSec = ControlGyroRightRateDegPerSec = ControlGyroMagnitudeDegPerSec = 0f;
            GyroIntegratedBankValid = false; GyroIntegratedBankWrappedDeg = GyroIntegratedBankDeltaDeg = 0f; GyroIntegratedBankConfidence = 0f;
            QuaternionDeltaValid = false; QuaternionDeltaLocalXDeg = QuaternionDeltaLocalYDeg = QuaternionDeltaLocalZDeg = 0f; QuaternionDeltaLocalXRateDegPerSec = QuaternionDeltaLocalYRateDegPerSec = QuaternionDeltaLocalZRateDegPerSec = 0f; QuaternionDeltaAngleDeg = QuaternionDeltaConfidence = 0f;
            QuaternionRollValid = false; QuaternionRollRateDegPerSec = QuaternionRollDeltaDeg = QuaternionRollWrappedDeg = 0f; QuaternionRollConfidence = 0f;
            EstimatorValid = false; EstimatorBankWrappedDeg = EstimatorBankDeltaDeg = 0f; EstimatorRollRateDegPerSec = EstimatorPitchRateDegPerSec = EstimatorYawRateDegPerSec = 0f; EstimatorConfidence = 0f;
            HorizonBankDeg = 0f; HorizonBankValid = false; HorizonBankConfidence = 0f;
            // Do not reset ControlBankUnwrappedDeg or the previous wrapped sample here:
            // Reset() runs every Unity update. Persistent tracker state is reset only when
            // the candidate becomes invalid or a new instrument instance is created.
        }


        static bool TryReadVanillaNavballHeading(out float heading)
        {
            heading = 0f;
            try
            {
                System.Type type = typeof(FlightGlobals);
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static;

                System.Reflection.PropertyInfo property = type.GetProperty("ship_heading", flags);
                object value = property != null ? property.GetValue(null, null) : null;
                if (value == null)
                {
                    System.Reflection.FieldInfo field = type.GetField("ship_heading", flags);
                    value = field != null ? field.GetValue(null) : null;
                }
                if (value == null) return false;

                heading = System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                return !float.IsNaN(heading) && !float.IsInfinity(heading);
            }
            catch
            {
                heading = 0f;
                return false;
            }
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        static float NormalizeSigned(float value) { return value > 180f ? value - 360f : value; }
        static float NormalizeWrapped(float value) { return Mathf.Repeat(value + 180f, 360f) - 180f; }
    }
}
