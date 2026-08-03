/* AERIS fixed-wing manager; AA-derived, GPL-3.0-or-later. */
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace AtmosphereAutopilot
{
    public sealed class TopModuleManager : StateController
    {
        Dictionary<Type, AutopilotModule> map;
        Dictionary<Type, StateController> high = new Dictionary<Type, StateController>();
        StateController activeController;
        StandardFlyByWire standard;
        float cachedAaStallSpeedMps;
        float cachedAaStallEstimateTime = -1f;
        float cachedAaStallMass;
        float cachedAaStallDensity;
        internal TopModuleManager(Vessel vessel) : base(vessel, "AERIS Fixed-Wing FBW Manager", 24888888)
        { map = AtmosphereAutopilot.Instance.autopilot_module_lists[vessel]; }
        public void create_context()
        {
            foreach (var type in AtmosphereAutopilot.Instance.autopilot_module_types)
            {
                if (type == typeof(TopModuleManager)) continue;
                var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Vessel) }, null);
                if (ctor == null) continue;
                map[type] = (AutopilotModule)ctor.Invoke(new object[] { vessel });
            }
            foreach (var pair in map) if (pair.Key != typeof(TopModuleManager)) { pair.Value.InitializeDependencies(map); pair.Value.Deserialize(); if(pair.Value is StateController) high[pair.Key]=(StateController)pair.Value; }
            standard = map[typeof(StandardFlyByWire)] as StandardFlyByWire;
            if (standard == null) throw new InvalidOperationException("StandardFlyByWire was not created.");
            activeController = standard;
        }
        protected override void OnActivate()
        {
            if (map.Count == 1) create_context();
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            if(activeController==null) activeController=standard;
            activeController.Activate();
            vessel.OnAutopilotUpdate += ApplyControl;
            Debug.Log("[AERIS-AA] active="+activeController.ModuleName+"; AA owns FlightCtrlState output.");
        }
        protected override void OnDeactivate()
        {
            foreach (var pair in map) if (pair.Key != typeof(TopModuleManager)) { pair.Value.Serialize(); pair.Value.Deactivate(); }
            vessel.OnAutopilotUpdate -= ApplyControl;
        }
        public override void ApplyControl(FlightCtrlState state)
        {
            if (vessel.state == Vessel.State.DEAD) return;
            ObserveAaStallSpeedEstimate();
            // Exactly one AA state controller owns FlightCtrlState per frame.
            // AERIS never overlays a second roll/yaw controller on top of AA native cruise.
            if (activeController != null && activeController.Active) activeController.ApplyControl(state);
        }
        public override void OnUpdate() { }
        public void SetMaster(bool active) { Active = active; }
        public bool IsStandardActive { get { return activeController==standard && standard != null && standard.Active; } }
        public StandardFlyByWire Standard { get { return standard; } }
        public CruiseController CruiseController { get { return map != null && map.ContainsKey(typeof(CruiseController)) ? map[typeof(CruiseController)] as CruiseController : null; } }
        public bool ActivateCruise()
        {
            var c = CruiseController;
            if (c == null) return false;
            if (!Active) Active = true;
            if (activeController != c)
            {
                if (activeController != null) activeController.Deactivate();
                c.Activate();
                activeController = c;
                var fm = FlightModel; if (fm != null) fm.sequential_dt = true;
            }
            return true;
        }
        public bool ActivateStandard()
        {
            if(standard==null) return false; if(!Active) Active=true;
            if(activeController!=standard){ if(activeController!=null) activeController.Deactivate(); standard.Activate(); activeController=standard; }
            return true;
        }
        public PitchAngularVelocityController PitchController { get { return map != null && map.ContainsKey(typeof(PitchAngularVelocityController)) ? map[typeof(PitchAngularVelocityController)] as PitchAngularVelocityController : null; } }
        public RollAngularVelocityController RollController { get { return map != null && map.ContainsKey(typeof(RollAngularVelocityController)) ? map[typeof(RollAngularVelocityController)] as RollAngularVelocityController : null; } }
        public YawAngularVelocityController YawController { get { return map != null && map.ContainsKey(typeof(YawAngularVelocityController)) ? map[typeof(YawAngularVelocityController)] as YawAngularVelocityController : null; } }
        public FlightModel FlightModel { get { return map != null && map.ContainsKey(typeof(FlightModel)) ? map[typeof(FlightModel)] as FlightModel : null; } }
        public ProgradeThrustController ThrustController { get { return map != null && map.ContainsKey(typeof(ProgradeThrustController)) ? map[typeof(ProgradeThrustController)] as ProgradeThrustController : null; } }

        // Hybrid Auto-Takeoff Vr source.  AA's learned pitch envelope supplies the
        // maximum lift acceleration at the current airborne condition.  The resulting
        // 1-g stall estimate is cached for the next ground roll and corrected for the
        // current mass/density.  If the model is untrained, implausible or stale, AERIS
        // reports failure and Auto Takeoff uses the configured manual Vr instead.
        public bool TryGetAaStallSpeedEstimate(out float stallSpeedMps, out string detail)
        {
            ObserveAaStallSpeedEstimate();
            stallSpeedMps = 0f;
            detail = "AA estimate unavailable";
            if (cachedAaStallEstimateTime < 0f || cachedAaStallSpeedMps <= 0f)
            {
                detail = "AA model not yet trained in airborne flight";
                return false;
            }
            float age = Time.realtimeSinceStartup - cachedAaStallEstimateTime;
            if (age < 0f || age > 900f)
            {
                detail = "AA estimate stale";
                return false;
            }
            float mass = vessel != null ? Mathf.Max(0.01f, (float)vessel.totalMass) : 0f;
            float density = vessel != null ? Mathf.Max(0.0001f, (float)vessel.atmDensity) : 0f;
            if (mass <= 0f || density <= 0f || cachedAaStallMass <= 0f || cachedAaStallDensity <= 0f)
            {
                detail = "mass/density correction unavailable";
                return false;
            }
            float correction = Mathf.Sqrt((mass / cachedAaStallMass) * (cachedAaStallDensity / density));
            if (float.IsNaN(correction) || float.IsInfinity(correction) || correction < 0.65f || correction > 1.55f)
            {
                detail = "mass/density correction out of range";
                return false;
            }
            float corrected = cachedAaStallSpeedMps * correction;
            if (corrected < 10f || corrected > 250f)
            {
                detail = "AA estimate outside 10..250 m/s";
                return false;
            }
            stallSpeedMps = corrected;
            detail = "AA learned lift envelope; age " + age.ToString("F0") + " s";
            return true;
        }

        void ObserveAaStallSpeedEstimate()
        {
            var fm = FlightModel;
            var pitch = PitchController;
            if (vessel == null || fm == null || pitch == null || vessel.LandedOrSplashed() || vessel.packed) return;
            float speed = (float)fm.surface_v_magnitude;
            float gravity = (float)vessel.graviticAcceleration.magnitude;
            float upper = pitch.res_equilibr_v_upper;
            float maxLiftAcceleration = (float)(upper * fm.surface_v_magnitude -
                fm.prev_pitch_gravity_acc - fm.prev_pitch_noninert_acc);
            float modelAge = Mathf.Abs(Time.fixedTime - fm.LastModelUpdateFixedTime);
            bool finite = !float.IsNaN(speed) && !float.IsInfinity(speed) &&
                !float.IsNaN(maxLiftAcceleration) && !float.IsInfinity(maxLiftAcceleration);
            if (!finite || fm.ModelUpdateSequence < 16 || modelAge > 0.25f || fm.dyn_pressure < 60.0 ||
                speed < 20f || gravity < 0.1f || maxLiftAcceleration < gravity * 1.05f) return;
            float estimate = speed * Mathf.Sqrt(gravity / maxLiftAcceleration);
            if (float.IsNaN(estimate) || float.IsInfinity(estimate) || estimate < 10f || estimate > 250f) return;
            cachedAaStallSpeedMps = estimate;
            cachedAaStallMass = Mathf.Max(0.01f, (float)vessel.totalMass);
            cachedAaStallDensity = Mathf.Max(0.0001f, (float)vessel.atmDensity);
            cachedAaStallEstimateTime = Time.realtimeSinceStartup;
        }
    }
}
