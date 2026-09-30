using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using AERISFlightControl.API;

namespace AERIS53RCore02RuntimeHarness
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class RuntimeHarness : MonoBehaviour
    {
        Rect windowRect = new Rect(60f, 80f, 560f, 430f);
        Vector2 scroll;
        string report = "Ready. Disable user-selected AERIS AP modes, then press RUN ALL 4 CASES.";
        bool running;

        void OnGUI()
        {
            windowRect = GUILayout.Window(
                GetInstanceID(),
                windowRect,
                DrawWindow,
                "AERIS53 R-CORE-02 Runtime Test");
        }

        void DrawWindow(int id)
        {
            GUILayout.Label("External Automation API omission/zero semantics");
            GUILayout.Label("Safety: each accepted mission is inspected immediately and cancelled before returning from this GUI callback.");

            if (!running && GUILayout.Button("RUN ALL 4 CASES", GUILayout.Height(42f)))
                RunAll();

            if (GUILayout.Button("COPY RESULT"))
            {
                try { GUIUtility.systemCopyBuffer = report ?? string.Empty; }
                catch { }
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(285f));
            GUILayout.TextArea(report ?? string.Empty, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Label("Expected final result: 4/4 PASS");
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        void RunAll()
        {
            running = true;
            var sb = new StringBuilder();
            AERISAutomationSession session = null;
            try
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (!HighLogic.LoadedSceneIsFlight || vessel == null)
                {
                    report = "PRECONDITION FAIL: enter Flight scene with an active vessel.";
                    return;
                }

                DirectorState baseline;
                string reflectError;
                if (!TryReadState(out baseline, out reflectError))
                {
                    report = "PRECONDITION FAIL: cannot inspect AERIS directors: " + reflectError;
                    return;
                }

                sb.AppendLine("AERIS53 R-CORE-02 RUNTIME");
                sb.AppendLine("baseline ALT armed=" + baseline.AltArmed +
                              " VEL armed=" + baseline.VelArmed +
                              " ALT target=" + baseline.AltTarget.ToString("0.0") +
                              " VEL target=" + baseline.VelTarget.ToString("0.0"));

                if (baseline.AltArmed || baseline.VelArmed)
                {
                    sb.AppendLine("PRECONDITION FAIL: ALT and VEL must both be disarmed before test.");
                    report = sb.ToString();
                    return;
                }

                var acquire = new AERISAutomationAcquireRequest
                {
                    Vessel = vessel,
                    VesselId = vessel.id,
                    ClientId = "AERIS53-RCORE02-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    DisplayName = "AERIS53 R-CORE-02 Runtime Harness",
                    Purpose = "R-CORE-02 omission and zero semantics",
                    Priority = AERISAutomationPriority.TaskAutomation,
                    RequestedCapabilities = new[] { AERISAutomationCapability.SetpointGuidance },
                    RequestedTtlSeconds = 120f,
                    AllowPilotOverride = true,
                    RequireProtect = false
                };

                AERISAutomationResult acquireResult;
                if (!AERISExternalAutomationApi.TryAcquire(acquire, out session, out acquireResult))
                {
                    sb.AppendLine("ACQUIRE FAIL: " + ResultText(acquireResult));
                    report = sb.ToString();
                    return;
                }
                sb.AppendLine("ACQUIRE PASS: " + ResultText(acquireResult));

                int pass = 0;
                if (RunAltitudeOnly(vessel, session, baseline, sb)) pass++;
                if (RunVelocityOnly(vessel, session, baseline, sb)) pass++;
                if (RunExplicitZero(vessel, session, baseline, sb)) pass++;
                if (RunEmptyRequest(vessel, session, baseline, sb)) pass++;

                sb.AppendLine();
                sb.AppendLine("RESULT: " + pass + "/4 " + (pass == 4 ? "PASS" : "FAIL"));
                report = sb.ToString();
                Debug.Log("[AERIS53_RCORE02_RUNTIME] " + report.Replace('\n', ' '));
            }
            catch (Exception ex)
            {
                sb.AppendLine("HARNESS EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
                report = sb.ToString();
                Debug.LogError("[AERIS53_RCORE02_RUNTIME] " + ex);
            }
            finally
            {
                if (session != null && session.SessionId != Guid.Empty)
                {
                    try
                    {
                        AERISAutomationResult releaseResult;
                        AERISExternalAutomationApi.TryRelease(session, out releaseResult);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[AERIS53_RCORE02_RUNTIME] release exception " + ex);
                    }
                }
                running = false;
            }
        }

        bool RunAltitudeOnly(Vessel vessel, AERISAutomationSession session,
            DirectorState baseline, StringBuilder sb)
        {
            int targetAltitude = 0;
            if (!double.IsNaN(vessel.altitude) && !double.IsInfinity(vessel.altitude))
            {
                double rounded = Math.Round(vessel.altitude);
                if (rounded < 0.0) rounded = 0.0;
                if (rounded > int.MaxValue) rounded = int.MaxValue;
                targetAltitude = (int)rounded;
            }

            var request = BaseRequest(vessel);
            request.AltitudeM = targetAltitude;

            AERISAutomationCommandHandle command;
            AERISAutomationResult result;
            bool accepted = AERISExternalAutomationApi.TrySubmitSetpointMission(
                session, request, out command, out result);

            DirectorState after;
            string error;
            bool inspected = TryReadState(out after, out error);
            bool pass = accepted && inspected &&
                        after.AltArmed &&
                        after.VelArmed == baseline.VelArmed &&
                        Math.Abs(after.AltTarget - targetAltitude) <= 0.11f;

            sb.AppendLine("CASE1 ALT ONLY: " + (pass ? "PASS" : "FAIL") +
                          " accepted=" + accepted +
                          " ALTarmed=" + (inspected ? after.AltArmed.ToString() : "?") +
                          " VELarmed=" + (inspected ? after.VelArmed.ToString() : "?") +
                          " target=" + targetAltitude +
                          " api=" + ResultText(result) +
                          (inspected ? "" : " reflect=" + error));

            CancelAndVerifyRestore(session, baseline, sb, "CASE1");
            return pass;
        }

        bool RunVelocityOnly(Vessel vessel, AERISAutomationSession session,
            DirectorState baseline, StringBuilder sb)
        {
            double targetSpeed = vessel.srfSpeed;
            if (double.IsNaN(targetSpeed) || double.IsInfinity(targetSpeed) || targetSpeed < 0.0)
                targetSpeed = 0.0;
            targetSpeed = Math.Round(Math.Min(targetSpeed, 5000.0), 1);

            var request = BaseRequest(vessel);
            request.SurfaceSpeedMps = targetSpeed;

            AERISAutomationCommandHandle command;
            AERISAutomationResult result;
            bool accepted = AERISExternalAutomationApi.TrySubmitSetpointMission(
                session, request, out command, out result);

            DirectorState after;
            string error;
            bool inspected = TryReadState(out after, out error);
            bool pass = accepted && inspected &&
                        after.VelArmed &&
                        after.AltArmed == baseline.AltArmed &&
                        Math.Abs(after.VelTarget - (float)targetSpeed) <= 0.11f;

            sb.AppendLine("CASE2 VEL ONLY: " + (pass ? "PASS" : "FAIL") +
                          " accepted=" + accepted +
                          " ALTarmed=" + (inspected ? after.AltArmed.ToString() : "?") +
                          " VELarmed=" + (inspected ? after.VelArmed.ToString() : "?") +
                          " target=" + targetSpeed.ToString("0.0") +
                          " api=" + ResultText(result) +
                          (inspected ? "" : " reflect=" + error));

            CancelAndVerifyRestore(session, baseline, sb, "CASE2");
            return pass;
        }

        bool RunExplicitZero(Vessel vessel, AERISAutomationSession session,
            DirectorState baseline, StringBuilder sb)
        {
            var request = BaseRequest(vessel);
            request.AltitudeM = 0;
            request.SurfaceSpeedMps = 0.0;

            AERISAutomationCommandHandle command;
            AERISAutomationResult result;
            bool accepted = AERISExternalAutomationApi.TrySubmitSetpointMission(
                session, request, out command, out result);

            DirectorState after;
            string error;
            bool inspected = TryReadState(out after, out error);
            bool pass = accepted && inspected &&
                        after.AltArmed && after.VelArmed &&
                        Math.Abs(after.AltTarget) <= 0.01f &&
                        Math.Abs(after.VelTarget) <= 0.01f;

            sb.AppendLine("CASE3 EXPLICIT ZERO: " + (pass ? "PASS" : "FAIL") +
                          " accepted=" + accepted +
                          " ALTarmed=" + (inspected ? after.AltArmed.ToString() : "?") +
                          " VELarmed=" + (inspected ? after.VelArmed.ToString() : "?") +
                          " ALTtarget=" + (inspected ? after.AltTarget.ToString("0.0") : "?") +
                          " VELtarget=" + (inspected ? after.VelTarget.ToString("0.0") : "?") +
                          " api=" + ResultText(result) +
                          (inspected ? "" : " reflect=" + error));

            CancelAndVerifyRestore(session, baseline, sb, "CASE3");
            return pass;
        }

        bool RunEmptyRequest(Vessel vessel, AERISAutomationSession session,
            DirectorState baseline, StringBuilder sb)
        {
            var request = BaseRequest(vessel);

            AERISAutomationCommandHandle command;
            AERISAutomationResult result;
            bool accepted = AERISExternalAutomationApi.TrySubmitSetpointMission(
                session, request, out command, out result);

            DirectorState after;
            string error;
            bool inspected = TryReadState(out after, out error);
            bool unchanged = inspected &&
                             after.AltArmed == baseline.AltArmed &&
                             after.VelArmed == baseline.VelArmed;
            bool pass = !accepted && result != null &&
                        result.ResultCode == AERISAutomationResultCode.InvalidRequest &&
                        unchanged;

            sb.AppendLine("CASE4 EMPTY REQUEST: " + (pass ? "PASS" : "FAIL") +
                          " accepted=" + accepted +
                          " code=" + (result == null ? "<null>" : result.ResultCode.ToString()) +
                          " ALTarmed=" + (inspected ? after.AltArmed.ToString() : "?") +
                          " VELarmed=" + (inspected ? after.VelArmed.ToString() : "?") +
                          " api=" + ResultText(result) +
                          (inspected ? "" : " reflect=" + error));
            return pass;
        }

        static AERISSetpointMissionRequest BaseRequest(Vessel vessel)
        {
            return new AERISSetpointMissionRequest
            {
                Vessel = vessel,
                VesselId = vessel.id,
                RequireStableCondition = false,
                CompletionPolicy = AERISSetpointCompletionPolicy.HoldUntilReplaced,
                ReplaceCurrentMission = false
            };
        }

        static void CancelAndVerifyRestore(AERISAutomationSession session,
            DirectorState baseline, StringBuilder sb, string label)
        {
            AERISAutomationResult cancel;
            bool cancelled = AERISExternalAutomationApi.TryCancelCurrentMission(
                session, "R-CORE-02 runtime harness immediate rollback", out cancel);

            DirectorState restored;
            string error;
            bool inspected = TryReadState(out restored, out error);
            bool restorePass = cancelled && inspected &&
                               restored.AltArmed == baseline.AltArmed &&
                               restored.VelArmed == baseline.VelArmed;
            sb.AppendLine("  " + label + " rollback: " +
                          (restorePass ? "PASS" : "FAIL") +
                          " api=" + ResultText(cancel) +
                          (inspected ? "" : " reflect=" + error));
        }

        static string ResultText(AERISAutomationResult result)
        {
            if (result == null) return "<null>";
            return result.ResultCode + "/" + (result.Detail ?? string.Empty);
        }

        struct DirectorState
        {
            public bool AltArmed;
            public bool VelArmed;
            public float AltTarget;
            public float VelTarget;
        }

        static bool TryReadState(out DirectorState state, out string error)
        {
            state = new DirectorState();
            error = null;
            try
            {
                Assembly assembly = typeof(AERISExternalAutomationApi).Assembly;
                Type bootstrapType = assembly.GetType("AERISFlightControl.Core.AERISBootstrap", true);
                FieldInfo instanceField = bootstrapType.GetField(
                    "instance", BindingFlags.Static | BindingFlags.NonPublic);
                if (instanceField == null)
                    throw new MissingFieldException("AERISBootstrap.instance");

                object bootstrap = instanceField.GetValue(null);
                if (bootstrap == null)
                    throw new InvalidOperationException("AERIS bootstrap is not ready.");

                object altitude = ReadProperty(
                    bootstrapType, bootstrap, "Altitude");
                object velocity = ReadProperty(
                    bootstrapType, bootstrap, "Velocity");
                if (altitude == null || velocity == null)
                    throw new InvalidOperationException("ALT/VEL director unavailable.");

                state.AltArmed = Convert.ToBoolean(ReadProperty(
                    altitude.GetType(), altitude, "Armed"));
                state.VelArmed = Convert.ToBoolean(ReadProperty(
                    velocity.GetType(), velocity, "Armed"));
                state.AltTarget = Convert.ToSingle(ReadProperty(
                    altitude.GetType(), altitude, "TargetAltitudeMeters"));
                state.VelTarget = Convert.ToSingle(ReadProperty(
                    velocity.GetType(), velocity, "TargetSurfaceSpeedMps"));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        static object ReadProperty(Type type, object instance, string name)
        {
            PropertyInfo property = type.GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(type.FullName, name);
            return property.GetValue(instance, null);
        }
    }
}
