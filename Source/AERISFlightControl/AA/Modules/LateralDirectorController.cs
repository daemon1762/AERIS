using System;
using System.Collections.Generic;
using UnityEngine;

namespace AtmosphereAutopilot
{
    /// <summary>
    /// AA-native lateral director.  It owns only the roll-axis request and deliberately
    /// never calls pitch/yaw controllers or Cruise/Director controllers.
    /// BANK: fixed bank target. HDG: heading error -> bank target -> AA roll controller.
    /// </summary>
    public sealed class LateralDirectorController : StateController
    {
        public enum LateralMode { None, Bank, Heading }

        internal LateralDirectorController(Vessel v) : base(v, "AERIS AA Lateral Director", 88437229) { }

        RollAngularVelocityController roll;
        [VesselSerializable("lateral_mode")] public LateralMode mode = LateralMode.None;
        [VesselSerializable("desired_bank")] public float desired_bank = 0f;
        [VesselSerializable("desired_heading")] public float desired_heading = 90f;
        [VesselSerializable("max_bank")] public float max_bank = 45f;

        // Tuned conservatively; AERIS exposes max_bank but AA owns the closed-loop roll path.
        public float heading_to_bank_gain = 0.28f;
        public float bank_capture_gain = 0.045f;
        public float heading_deadband_deg = 0.75f;
        public float bank_deadband_deg = 0.50f;
        public float max_roll_rate = 0.42f;
        public float turn_rate_damping = 0.35f;

        public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
        {
            roll = modules[typeof(RollAngularVelocityController)] as RollAngularVelocityController;
        }

        public float CurrentHeading
        {
            get
            {
                if (vessel == null || vessel.mainBody == null) return 0f;
                Vector3d up = (vessel.CoM - vessel.mainBody.position).normalized;
                Vector3d north = Vector3d.Exclude(up, vessel.mainBody.RotationAxis).normalized;
                Vector3d forward = Vector3d.Exclude(up, vessel.transform.forward).normalized;
                if (north.sqrMagnitude < 1e-8 || forward.sqrMagnitude < 1e-8) return 0f;
                double sin = Vector3d.Dot(Vector3d.Cross(north, forward), up);
                double cos = Vector3d.Dot(north, forward);
                return Mathf.Repeat((float)(Math.Atan2(sin, cos) * Mathf.Rad2Deg), 360f);
            }
        }

        public float CurrentBank
        {
            get
            {
                if (vessel == null || vessel.mainBody == null) return 0f;
                Vector3d up = (vessel.CoM - vessel.mainBody.position).normalized;
                Vector3d forward = Vector3d.Exclude(up, vessel.transform.forward).normalized;
                if (forward.sqrMagnitude < 1e-8) forward = vessel.transform.forward.normalized;
                Vector3d craftUp = vessel.transform.up.normalized;
                double sin = Vector3d.Dot(Vector3d.Cross(up, craftUp), forward);
                double cos = Vector3d.Dot(up, craftUp);
                return (float)(Math.Atan2(sin, cos) * Mathf.Rad2Deg);
            }
        }

        public float CurrentTurnRate
        {
            get
            {
                if (vessel == null || vessel.mainBody == null) return 0f;
                Vector3d up = (vessel.CoM - vessel.mainBody.position).normalized;
                return (float)Vector3d.Dot(vessel.angularVelocity, up) * Mathf.Rad2Deg;
            }
        }

        public bool IsBankHold { get { return Active && mode == LateralMode.Bank; } }
        public bool IsHeadingHold { get { return Active && mode == LateralMode.Heading; } }
        public float ActiveTargetBank { get; private set; }
        public float LastHeadingError { get; private set; }
        public float LastRollCommand { get; private set; }

        public void SetBankMode(float targetBank)
        {
            desired_bank = Mathf.Clamp(targetBank, -85f, 85f);
            mode = LateralMode.Bank;
        }

        public void SetHeadingMode(float targetHeading)
        {
            desired_heading = Mathf.Repeat(targetHeading, 360f);
            mode = LateralMode.Heading;
        }

        protected override void OnActivate()
        {
            if (roll != null) roll.user_controlled = false;
        }

        protected override void OnDeactivate()
        {
            mode = LateralMode.None;
            ActiveTargetBank = 0f;
            LastHeadingError = 0f;
            LastRollCommand = 0f;
            if (roll != null) roll.user_controlled = true;
        }

        public override void ApplyControl(FlightCtrlState state)
        {
            if (vessel == null || vessel.LandedOrSplashed() || roll == null || mode == LateralMode.None) return;

            float targetBank;
            if (mode == LateralMode.Heading)
            {
                float headingError = Mathf.DeltaAngle(CurrentHeading, desired_heading);
                if (Mathf.Abs(headingError) < heading_deadband_deg) headingError = 0f;
                LastHeadingError = headingError;
                // Heading error creates a bounded bank request. Existing turn rate softens capture.
                targetBank = headingError * heading_to_bank_gain - CurrentTurnRate * turn_rate_damping;
            }
            else
            {
                LastHeadingError = 0f;
                targetBank = desired_bank;
            }

            targetBank = Mathf.Clamp(targetBank, -Mathf.Abs(max_bank), Mathf.Abs(max_bank));
            ActiveTargetBank = targetBank;
            float bankError = Mathf.DeltaAngle(CurrentBank, targetBank);
            if (Mathf.Abs(bankError) < bank_deadband_deg) bankError = 0f;
            float desiredRate = Mathf.Clamp(bankError * bank_capture_gain, -max_roll_rate, max_roll_rate);

            // Hard stop: when outside the allowed envelope, only command recovery toward it.
            float currentBank = CurrentBank;
            if (currentBank > max_bank && desiredRate > 0f) desiredRate = 0f;
            if (currentBank < -max_bank && desiredRate < 0f) desiredRate = 0f;

            LastRollCommand = desiredRate;
            roll.user_controlled = false;
            roll.ApplyControl(state, desiredRate);
        }
    }
}
