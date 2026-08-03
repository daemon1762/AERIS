/*
AERIS Flight Control — AA-derived fixed-wing core.
Derived from AtmosphereAutopilot, Copyright (C) 2015-2016 Baranin Alexander / Boris-Barboris and contributors.
Licensed under GPL-3.0-or-later. See NOTICE and LICENSE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AtmosphereAutopilot
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class AtmosphereAutopilot : MonoBehaviour
    {
        internal List<Type> autopilot_module_types = new List<Type>();
        internal Dictionary<Vessel, Dictionary<Type, AutopilotModule>> autopilot_module_lists =
            new Dictionary<Vessel, Dictionary<Type, AutopilotModule>>(new VesselOldComparator());
        internal AutoHotkey hotkeyManager;
        public static AtmosphereAutopilot Instance { get; private set; }
        public Vessel ActiveVessel { get; private set; }
        public enum AerodinamycsModel { Stock, FAR }
        public static AerodinamycsModel AeroModel { get; private set; }
        public static Dictionary<Type, ConstructorInfo> gimbal_module_wrapper_map = new Dictionary<Type, ConstructorInfo>(4);
        BackgroundThread thread;
        bool duplicateInstance;
        public BackgroundThread BackgroundThread { get { return thread; } }
        public static bool UIHidden = false;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                duplicateInstance = true;
                Debug.LogWarning("[AERIS-AA] Duplicate persistent core host rejected.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (duplicateInstance) return;
            autopilot_module_types.Clear();
            autopilot_module_lists.Clear();
            gimbal_module_wrapper_map.Clear();
            determine_aerodynamics();
            get_csurf_module();
            get_gimbal_modules();
            initialize_types();
            hotkeyManager = new AutoHotkey(autopilot_module_types);
            thread = new BackgroundThread(KSPUtil.ApplicationRootPath + "GameData/AERISFlightControl");
            GameEvents.onVesselChange.Add(vesselSwitch);
            GameEvents.onGameSceneLoadRequested.Add(sceneSwitch);
            GameEvents.onGamePause.Add(OnApplicationPause);
            GameEvents.onGameUnpause.Add(OnApplicationUnpause);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            Debug.Log("[AERIS-AA] Fixed-wing core host started. Legacy AA UI and non-fixed-wing modes are excluded.");
        }
        void determine_aerodynamics()
        {
            AeroModel = AerodinamycsModel.Stock;
            try { if (AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch")) AeroModel = AerodinamycsModel.FAR; }
            catch { AeroModel = AerodinamycsModel.Stock; }
        }
        /// <summary>
        /// Active controllable-surface module type. Restored from upstream AA because
        /// the FlightModel uses it to determine aerodynamic control authority.
        /// </summary>
        public static Type control_surface_module_type { get; private set; }

        void get_csurf_module()
        {
            if (AeroModel == AerodinamycsModel.Stock)
            {
                control_surface_module_type = typeof(SyncModuleControlSurface);
                return;
            }

            try
            {
                var farAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "FerramAerospaceResearch");
                control_surface_module_type = farAssembly == null ? null :
                    farAssembly.GetTypes().FirstOrDefault(t => t.Name == "FARControllableSurface");
            }
            catch
            {
                control_surface_module_type = null;
            }
        }
        void get_gimbal_modules()
        {
            try { gimbal_module_wrapper_map.Add(typeof(ModuleGimbal), typeof(StockGimbal).GetConstructors()[0]); }
            catch { }
            try { if (kmGimbal.do_reflections()) gimbal_module_wrapper_map.Add(kmGimbal.gimbal_type, typeof(kmGimbal).GetConstructors()[0]); }
            catch { }
        }
        void initialize_types()
        {
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(AutopilotModule)) && t.IsSealed)) autopilot_module_types.Add(t);
        }
        void OnApplicationPause() { if (thread != null) thread.Pause(); }
        void OnApplicationUnpause() { if (thread != null) thread.Resume(); }
        void OnHideUI() { UIHidden = true; }
        void OnShowUI() { UIHidden = false; }
        void sceneSwitch(GameScenes s)
        {
            serialize_active_modules(); clean_modules();
            if (s != GameScenes.FLIGHT) { ActiveVessel = null; if (thread != null) thread.Pause(); }
            else if (thread != null) thread.Resume();
        }
        void vesselSwitch(Vessel v)
        {
            serialize_active_modules();
            if (v == null) { ActiveVessel = null; return; }
            load_manager_for_vessel(v); ActiveVessel = v;
            foreach (var map in autopilot_module_lists.Values)
                if (map.ContainsKey(typeof(FlightModel))) (map[typeof(FlightModel)] as FlightModel).sequential_dt = false;
        }
        void load_manager_for_vessel(Vessel v)
        {
            if (!autopilot_module_lists.ContainsKey(v)) autopilot_module_lists[v] = new Dictionary<Type, AutopilotModule>();
            if (!autopilot_module_lists[v].ContainsKey(typeof(TopModuleManager))) autopilot_module_lists[v][typeof(TopModuleManager)] = new TopModuleManager(v);
        }
        void serialize_active_modules()
        {
            if (ActiveVessel == null || !autopilot_module_lists.ContainsKey(ActiveVessel)) return;
            foreach (var m in autopilot_module_lists[ActiveVessel].Values) m.Serialize();
        }
        void clean_modules()
        {
            foreach (var v in autopilot_module_lists.Keys.Where(v => v == null || v.state == Vessel.State.DEAD).ToArray()) autopilot_module_lists.Remove(v);
        }
        public Dictionary<Type, AutopilotModule> getVesselModules(Vessel v) { return v != null && autopilot_module_lists.ContainsKey(v) ? autopilot_module_lists[v] : null; }
        public void setLauncherOnOffIcon(bool state) { }
        public void mainMenuGUIUpdate() { }
        public void mainMenuGUISpeedUpdate() { }
        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || ActiveVessel == null) return;
            Dictionary<Type, AutopilotModule> map;
            if (!autopilot_module_lists.TryGetValue(ActiveVessel, out map)) return;
            foreach (var m in map.Values.ToList()) if (m.Active || m is TopModuleManager) m.OnUpdate();
        }
        void OnDestroy()
        {
            if (duplicateInstance || Instance != this) return;
            try { serialize_active_modules(); } catch { }
            try { if (thread != null) thread.Stop(); } catch { }
            try { GameEvents.onVesselChange.Remove(vesselSwitch); } catch { }
            try { GameEvents.onGameSceneLoadRequested.Remove(sceneSwitch); } catch { }
            try { GameEvents.onGamePause.Remove(OnApplicationPause); } catch { }
            try { GameEvents.onGameUnpause.Remove(OnApplicationUnpause); } catch { }
            try { GameEvents.onHideUI.Remove(OnHideUI); } catch { }
            try { GameEvents.onShowUI.Remove(OnShowUI); } catch { }
            autopilot_module_lists.Clear();
            ActiveVessel = null;
            thread = null;
            Instance = null;
        }
    }
}
