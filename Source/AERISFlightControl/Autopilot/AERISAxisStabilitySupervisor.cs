using System;
using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // v0.4.61 observation foundation. This class never modifies AERIS director
    // output, AA controller state, or final FlightCtrlState. It only classifies
    // oscillation signatures so later mitigation can be enabled one axis at a time.
    internal sealed class AERISAxisStabilitySupervisor
    {
        internal string RollState { get; private set; } = "IDLE";
        internal string PitchState { get; private set; } = "IDLE";
        internal string YawState { get; private set; } = "IDLE";
        internal int RollReversals { get; private set; }
        internal int PitchReversals { get; private set; }
        internal int YawReversals { get; private set; }
        internal float RollMitigationScale { get { return 1f; } }
        internal float PitchMitigationScale { get { return 1f; } }
        internal float YawMitigationScale { get { return 1f; } }

        const float WindowSeconds = 1.25f;
        const float InputThreshold = 0.025f;
        const float RateThreshold = 0.40f;
        const int CautionReversals = 3;
        const int OscillationReversals = 5;
        readonly AxisMonitor roll = new AxisMonitor();
        readonly AxisMonitor pitch = new AxisMonitor();
        readonly AxisMonitor yaw = new AxisMonitor();
        float lastTrace;

        internal void Update(AERISBankDirector bank, AERISPitchDirector pitchDirector, AERISHdgDirector hdg, VirtualAttitudeInstrument attitude)
        {
            if (attitude == null) return;
            float now = Time.realtimeSinceStartup;
            // BANK v0.4.88 transport is AA's native roll-rate command (rad/s), not a virtual stick.
            roll.Update(now, bank != null ? bank.AaNativeRollRateDemandRadPerSec : 0f, attitude.InstrumentRollRateDegPerSec, InputThreshold, RateThreshold);
            // v0.4.91 pitch transport is AA-native desired angular velocity, not a virtual stick.
            pitch.Update(now, pitchDirector != null ? pitchDirector.AaNativePitchRateDemandRadPerSec : 0f, attitude.InstrumentPitchRateDegPerSec, InputThreshold, RateThreshold);
            // v0.4.90 yaw transport is AA-native desired angular velocity, not a virtual rudder input.
            yaw.Update(now, hdg != null ? hdg.AaNativeYawRateDemandRadPerSec : 0f, attitude.InstrumentYawRateDegPerSec, InputThreshold, RateThreshold);
            RollReversals = roll.Reversals; PitchReversals = pitch.Reversals; YawReversals = yaw.Reversals;
            RollState = StateFor(roll); PitchState = StateFor(pitch); YawState = StateFor(yaw);
            if (now - lastTrace >= 1.0f && (RollState == "OSCILLATION" || PitchState == "OSCILLATION" || YawState == "OSCILLATION"))
            {
                lastTrace = now;
                AERISLogger.Info("[AXIS_SUPERVISOR][OBSERVE] roll=" + RollState + "/" + RollReversals +
                    " pitch=" + PitchState + "/" + PitchReversals + " yaw=" + YawState + "/" + YawReversals +
                    " — no control mitigation applied.");
            }
        }

        static string StateFor(AxisMonitor m)
        {
            if (m.Reversals >= OscillationReversals) return "OSCILLATION";
            if (m.Reversals >= CautionReversals) return "CAUTION";
            return m.Active ? "STABLE" : "IDLE";
        }

        sealed class AxisMonitor
        {
            readonly Queue<float> reversalTimes = new Queue<float>();
            int previousInputSign;
            int previousRateSign;
            internal int Reversals { get; private set; }
            internal bool Active { get; private set; }
            internal void Update(float now, float input, float rate, float inputThreshold, float rateThreshold)
            {
                int inputSign = Sign(input, inputThreshold);
                int rateSign = Sign(rate, rateThreshold);
                Active = inputSign != 0 || rateSign != 0;
                bool reverse = (inputSign != 0 && previousInputSign != 0 && inputSign != previousInputSign) ||
                               (rateSign != 0 && previousRateSign != 0 && rateSign != previousRateSign);
                if (reverse) reversalTimes.Enqueue(now);
                if (inputSign != 0) previousInputSign = inputSign;
                if (rateSign != 0) previousRateSign = rateSign;
                while (reversalTimes.Count > 0 && now - reversalTimes.Peek() > WindowSeconds) reversalTimes.Dequeue();
                Reversals = reversalTimes.Count;
            }
            static int Sign(float v, float threshold) { return v > threshold ? 1 : (v < -threshold ? -1 : 0); }
        }
    }
}
