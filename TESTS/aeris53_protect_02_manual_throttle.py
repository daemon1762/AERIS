#!/usr/bin/env python3
"""Compile the real StandardFlyByWire against isolated KSP/AA boundaries.

No test DLL is installed in GameData. Requires mcs and mono, as does the build.
"""
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl/AA/Modules/StandardFlyByWire.cs"

# Axis controllers are inert here; the production throttle arbitration, state,
# input mirroring, ceiling and propulsion callback all execute unchanged.
HARNESS = r"""
using System;
using System.Collections.Generic;
using AtmosphereAutopilot;

namespace UnityEngine {
    public enum KeyCode { None, O }
    public static class Input { public static bool GetKeyDown(KeyCode key) { return false; } }
    public static class Mathf {
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Clamp(float v, float lo, float hi) { return Math.Max(lo, Math.Min(hi, v)); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
    public struct Vector3 {
        public Vector3 normalized { get { return this; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a) { return a; }
        public static Vector3 operator *(float f, Vector3 v) { return v; }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { return v; }
        public static float Dot(Vector3 a, Vector3 b) { return 0f; }
    }
    public class Transform { public Vector3 position, up, right, forward; }
}
public class FlightCtrlState { public float mainThrottle, pitch, roll, yaw; }
public static class FlightInputHandler { public static FlightCtrlState state; }
public static class TimeWarp { public static float fixedDeltaTime = 0.02f; }
public class Body { public UnityEngine.Vector3 position; }
public class Vessel {
    public bool grounded;
    public UnityEngine.Transform ReferenceTransform = new UnityEngine.Transform();
    public Body mainBody = new Body();
    public bool LandedOrSplashed() { return grounded; }
}
namespace AERISFlightControl.Logging {
    public static class AERISLogger { public static void Info(string message) { } }
}
namespace AtmosphereAutopilot {
    public class VesselSerializable : Attribute { public VesselSerializable(string name) { } }
    public class GlobalSerializable : Attribute { public GlobalSerializable(string name) { } }
    public class AutoGuiAttr : Attribute { public AutoGuiAttr(string name, bool value) { } }
    public class AutoHotkeyAttr : Attribute { public AutoHotkeyAttr(string name) { } }
    public class AutopilotModule {
        public bool user_controlled;
        public void Activate() { }
        public void Deactivate() { }
    }
    public class StateController : AutopilotModule {
        protected const int YAW = 2;
        protected Vessel vessel;
        public StateController(Vessel v, string name, int id) { vessel = v; }
        public virtual void InitializeDependencies(Dictionary<Type, AutopilotModule> modules) { }
        protected virtual void OnActivate() { }
        protected virtual void OnDeactivate() { }
        public virtual void OnUpdate() { }
        public virtual void ApplyControl(FlightCtrlState state) { }
        protected virtual void _drawGUI(int id) { }
    }
    public class PitchAngularVelocityController : AutopilotModule {
        public bool moderate_aoa = true, moderate_g = true;
        public float neutral_offset;
        public bool ExternalRateControlActive, ModerationEnvelopeAvailable, DesiredAngularVelocityModerated;
        public float RequestedDesiredAngularVelocity, AppliedDesiredAngularVelocity;
        public float ModerationLowerAngularVelocity, ModerationUpperAngularVelocity;
        public void ApplyControl(FlightCtrlState state, float demand) { }
    }
    public class RollAngularVelocityController : AutopilotModule {
        public void ApplyControl(FlightCtrlState state, float demand) { }
    }
    public class YawAngularVelocityController : AutopilotModule {
        public bool moderate_aoa = true, moderate_g = true;
        public void ApplyControl(FlightCtrlState state, float demand) { }
    }
    public class SideslipController : AutopilotModule {
        public void ApplyControl(FlightCtrlState state, float a, float b) { }
    }
    public class Speed { public float mps() { return 100f; } }
    public class ProgradeThrustController : AutopilotModule {
        public bool spd_control_enabled;
        public float demand = 0.45f;
        public Speed setpoint = new Speed();
        public void ApplyControl(FlightCtrlState state, float speed) { state.mainThrottle = demand; }
    }
    public class FlightModel : AutopilotModule { public float AngularVel(int axis) { return 0f; } }
    public static class ControlUtils {
        public const int PITCH = 0, ROLL = 1, YAW = 2;
        public static void neutralize_user_input(FlightCtrlState state, int axis) { }
    }
    public static class MessageManager { public static void post_status_message(string message) { } }
    public class AtmosphereAutopilot {
        public static AtmosphereAutopilot Instance = new AtmosphereAutopilot();
        public void mainMenuGUIUpdate() { }
    }
}

class Fixture {
    public Vessel vessel = new Vessel();
    public ProgradeThrustController speed = new ProgradeThrustController();
    public StandardFlyByWire fbw;
    public Fixture(float manual = 0.2f) {
        StandardFlyByWire.ExternalThrottleOverride = false;
        StandardFlyByWire.ExternalThrottleDemand = 0f;
        StandardFlyByWire.ExternalThrottleCeiling = null;
        StandardFlyByWire.ExternalThrottleFloor = null;
        StandardFlyByWire.ExternalPropulsionDemand = null;
        StandardFlyByWire.ExternalPitchOverride = false;
        StandardFlyByWire.ExternalYawOverride = false;
        StandardFlyByWire.ExternalRollOverride = false;
        StandardFlyByWire.ExternalTrimFeedForwardActive = false;
        FlightInputHandler.state = new FlightCtrlState { mainThrottle = manual };
        fbw = new StandardFlyByWire(vessel);
        fbw.InitializeDependencies(new Dictionary<Type, AutopilotModule> {
            { typeof(PitchAngularVelocityController), new PitchAngularVelocityController() },
            { typeof(RollAngularVelocityController), new RollAngularVelocityController() },
            { typeof(YawAngularVelocityController), new YawAngularVelocityController() },
            { typeof(SideslipController), new SideslipController() },
            { typeof(ProgradeThrustController), speed },
            { typeof(FlightModel), new FlightModel() }
        });
    }
    public float Frame(float floor, float? input = null) {
        if (input.HasValue) FlightInputHandler.state.mainThrottle = input.Value;
        StandardFlyByWire.ExternalThrottleFloor = x => floor;
        var state = new FlightCtrlState { mainThrottle = FlightInputHandler.state.mainThrottle };
        fbw.ApplyControl(state);
        Test.Equal("both throttle channels agree", FlightInputHandler.state.mainThrottle, state.mainThrottle);
        return state.mainThrottle;
    }
}

class Test {
    static int passed, failed;
    public static void Equal(string message, float actual, float expected) {
        if (Math.Abs(actual - expected) > 0.000002f)
            throw new Exception(message + ": expected " + expected + ", got " + actual);
    }
    static void Run(string name, Action test) {
        try { test(); ++passed; Console.WriteLine("PASS: " + name); }
        catch (Exception e) { ++failed; Console.WriteLine("FAIL: " + name + " -- " + e.Message); }
    }
    static int Main() {
        Run("Shift increases above the active Protect floor", () => {
            var f = new Fixture(); f.Frame(0.4f);
            Equal("first increase", f.Frame(0.4f, 0.41f), 0.41f);
            Equal("continued increase", f.Frame(0.4f, 0.42f), 0.42f);
        });
        Run("Z reaches full throttle while Protect owns throttle", () => {
            var f = new Fixture(0.0249f); f.Frame(0.0258f);
            Equal("FDR full-throttle input", f.Frame(0.0280f, 1f), 1f);
            Equal("release retains accepted pilot demand", f.Frame(0f), 1f);
        });
        Run("small Shift increases are accepted without a deadband", () => {
            var f = new Fixture(); f.Frame(0.4f);
            Equal("small increase", f.Frame(0.4f, 0.40001f), 0.40001f);
        });
        Run("a rising floor may exceed a newly accepted pilot increase", () => {
            var f = new Fixture(); f.Frame(0.4f);
            Equal("floor remains authoritative", f.Frame(0.6f, 0.41f), 0.6f);
            Equal("release restores pilot increase", f.Frame(0f), 0.41f);
        });
        Run("unchanged Protect echoes never become the manual baseline", () => {
            var f = new Fixture(); f.Frame(0.4f); f.Frame(0.6f); f.Frame(0.3f);
            Equal("release restores pre-assist input", f.Frame(0f), 0.2f);
        });
        Run("pilot decreases below the floor preserve the recovery floor", () => {
            var f = new Fixture(); f.Frame(0.4f);
            Equal("floor", f.Frame(0.4f, 0f), 0.4f);
            Equal("original release baseline", f.Frame(0f), 0.2f);
        });
        Run("normal manual throttle still accepts increases and decreases", () => {
            var f = new Fixture(); Equal("increase", f.Frame(0f, 1f), 1f);
            Equal("decrease", f.Frame(0f, 0.1f), 0.1f);
        });
        Run("SPD ownership and its release baseline are unchanged", () => {
            var f = new Fixture(); f.speed.spd_control_enabled = true; f.Frame(0.4f);
            Equal("SPD demand", f.Frame(0.4f, 1f), 0.45f);
            Equal("Protect above SPD", f.Frame(0.7f, 1f), 0.7f);
            f.speed.spd_control_enabled = false;
            Equal("release", f.Frame(0f), 0.2f);
        });
        Run("ACC ownership and its release baseline are unchanged", () => {
            var f = new Fixture(); StandardFlyByWire.ExternalThrottleOverride = true;
            StandardFlyByWire.ExternalThrottleDemand = 0.5f; f.Frame(0.4f);
            Equal("ACC demand", f.Frame(0.4f, 1f), 0.5f);
            Equal("Protect above ACC", f.Frame(0.7f, 1f), 0.7f);
            StandardFlyByWire.ExternalThrottleOverride = false;
            Equal("release", f.Frame(0f), 0.2f);
        });
        Run("external release baseline remains authoritative", () => {
            var f = new Fixture(); f.Frame(0.4f);
            StandardFlyByWire.SetExternalThrottleReleaseBaseline(0.1f);
            Equal("release", f.Frame(0f), 0.1f);
        });
        Run("accepted pilot increase still passes through ceiling and propulsion", () => {
            var f = new Fixture(); f.Frame(0.4f);
            float propulsion = -1f;
            StandardFlyByWire.ExternalThrottleCeiling = x => Math.Min(x, 0.7f);
            StandardFlyByWire.ExternalPropulsionDemand = x => propulsion = x;
            Equal("ceiling", f.Frame(0.4f, 1f), 0.7f);
            Equal("propulsion receives final command", propulsion, 0.7f);
        });
        Run("ground ACC demand and baseline restoration are unchanged", () => {
            var f = new Fixture(); f.vessel.grounded = true;
            StandardFlyByWire.ExternalThrottleOverride = true;
            StandardFlyByWire.ExternalThrottleDemand = 0.5f; f.Frame(0.7f);
            Equal("ground demand", f.Frame(0.7f, 1f), 0.5f);
            StandardFlyByWire.ExternalThrottleOverride = false;
            StandardFlyByWire.ExternalRollOverride = true;
            Equal("ground release", f.Frame(0.7f), 0.2f);
        });
        Run("a ground-to-air ownership transition does not capture its own echo", () => {
            var f = new Fixture(); f.vessel.grounded = true;
            StandardFlyByWire.ExternalThrottleOverride = true;
            StandardFlyByWire.ExternalThrottleDemand = 0.5f; f.Frame(0f);
            StandardFlyByWire.ExternalThrottleOverride = false; f.vessel.grounded = false;
            Equal("airborne floor", f.Frame(0.4f), 0.4f);
            Equal("original baseline", f.Frame(0f), 0.2f);
        });
        Run("another vessel's telemetry cannot turn a Protect echo into input", () => {
            var f = new Fixture(); f.Frame(0.6f);
            var other = new Fixture(0.05f); other.Frame(0f);
            f.Frame(0.6f, 0.6f);
            Equal("original baseline", f.Frame(0f), 0.2f);
        });
        Console.WriteLine("[AERIS53 PROTECT-02 MANUAL THROTTLE] " + passed + "/" + (passed + failed) + " PASS");
        return failed == 0 ? 0 : 1;
    }
}
"""


def main():
    for tool in ("mcs", "mono"):
        if shutil.which(tool) is None:
            raise SystemExit(f"STOP: {tool} is required")
    with tempfile.TemporaryDirectory(prefix="aeris-protect02-") as folder:
        folder = Path(folder)
        harness = folder / "ThrottleRegression.cs"
        harness.write_text(HARNESS, encoding="utf-8")
        exe = folder / "ThrottleRegression.exe"
        subprocess.run(["mcs", "-out:" + str(exe), str(SOURCE), str(harness)], check=True)
        return subprocess.run(["mono", str(exe)]).returncode


if __name__ == "__main__":
    raise SystemExit(main())
