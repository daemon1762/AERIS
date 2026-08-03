using UnityEngine;
using ToolbarControl_NS;
using KSP.UI.Screens;

namespace AERISFlightControl.UI
{
    // FMJ-style lifecycle: one persistent owner, one managed icon, and scene-safe
    // launcher rebinding. ToolbarControl continues to bridge stock/blizzy toolbars,
    // while AERIS owns the visible-state contract and rejects duplicate owners.
    internal sealed class ToolbarBridge : MonoBehaviour
    {
        internal const string ModId = "AERISFlightControl";
        internal const string ModName = "AERIS Flight Control";
        internal const string StockIconPath = "AERISFlightControl/Icons/aeris_icon_38";
        internal const string BlizzyIconPath = "AERISFlightControl/Icons/aeris_icon_24";
        internal const string StockTextureName = "AERIS.ToolbarIcon.Stock38";
        internal const string BlizzyTextureName = "AERIS.ToolbarIcon.Blizzy24";

        private static ToolbarBridge owner;
        private static bool modRegistered;

        private ToolbarControl toolbar;
        private System.Action onShow;
        private System.Action onHide;
        private bool launcherEventsBound;
        private bool hasAppliedVisibleState;
        private bool lastVisibleState;
        private bool shuttingDown;
        private int launcherGeneration;

        internal static void RegisterModOnce()
        {
            if (modRegistered) return;
            ToolbarControl.RegisterMod(ModId, ModName);
            modRegistered = true;
        }

        internal void Initialise(System.Action show, System.Action hide)
        {
            if (toolbar != null) return;
            if (owner != null && owner != this)
            {
                Debug.LogWarning("[AERIS] Duplicate ToolbarBridge initialization rejected; existing owner retained.");
                return;
            }

            owner = this;
            onShow = show;
            onHide = hide;
            BindLauncherEvents();
            NameManagedIcon(StockIconPath, StockTextureName);
            NameManagedIcon(BlizzyIconPath, BlizzyTextureName);

            toolbar = gameObject.AddComponent<ToolbarControl>();
            ApplicationLauncher.AppScenes scenes =
                ApplicationLauncher.AppScenes.ALWAYS |
                ApplicationLauncher.AppScenes.TRACKSTATION;

            toolbar.AddToAllToolbars(
                HandleShow,
                HandleHide,
                scenes,
                ModId,
                "AERISMainButton",
                StockIconPath,
                BlizzyIconPath,
                ModName);

            Debug.Log("[AERIS] Toolbar owner initialized once; scenes=ALWAYS|TRACKSTATION stock=" +
                StockIconPath + " blizzy=" + BlizzyIconPath + " ToolbarController=" +
                typeof(ToolbarControl).Assembly.GetName().Version);
        }

        private void BindLauncherEvents()
        {
            if (launcherEventsBound) return;
            GameEvents.onGUIApplicationLauncherReady.Add(HandleLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(HandleLauncherDestroyed);
            launcherEventsBound = true;
        }

        private void UnbindLauncherEvents()
        {
            if (!launcherEventsBound) return;
            GameEvents.onGUIApplicationLauncherReady.Remove(HandleLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(HandleLauncherDestroyed);
            launcherEventsBound = false;
        }

        private void HandleLauncherReady()
        {
            launcherGeneration++;
            hasAppliedVisibleState = false;
            Debug.Log("[AERIS] Toolbar launcher ready; rebind generation=" + launcherGeneration +
                " scene=" + HighLogic.LoadedScene);
        }

        private void HandleLauncherDestroyed()
        {
            launcherGeneration++;
            hasAppliedVisibleState = false;
            Debug.Log("[AERIS] Toolbar launcher destroyed; references invalidated generation=" +
                launcherGeneration + " scene=" + HighLogic.LoadedScene);
        }

        private static void NameManagedIcon(string path, string stableName)
        {
            try
            {
                if (GameDatabase.Instance == null) return;
                Texture2D texture = GameDatabase.Instance.GetTexture(path, false);
                if (texture != null) texture.name = stableName;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AERIS] Toolbar icon attribution naming skipped: " + ex.Message);
            }
        }

        private void HandleShow()
        {
            if (shuttingDown) return;
            hasAppliedVisibleState = true;
            lastVisibleState = true;
            if (onShow != null) onShow();
        }

        private void HandleHide()
        {
            if (shuttingDown) return;
            hasAppliedVisibleState = true;
            lastVisibleState = false;
            if (onHide != null) onHide();
        }

        internal void SetState(bool visible)
        {
            if (toolbar == null || shuttingDown) return;
            if (hasAppliedVisibleState && lastVisibleState == visible) return;

            hasAppliedVisibleState = true;
            lastVisibleState = visible;
            if (visible) toolbar.SetTrue(false);
            else toolbar.SetFalse(false);
        }

        internal void InvalidateSceneBinding()
        {
            // Called at the AERIS scene boundary as an additional guard. The stock
            // launcher may be destroyed/recreated after this frame; the ready/destroyed
            // events above repeat the invalidation when that happens.
            launcherGeneration++;
            hasAppliedVisibleState = false;
        }

        internal void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            UnbindLauncherEvents();
            onShow = null;
            onHide = null;
            hasAppliedVisibleState = false;

            // Do not invoke ToolbarControl.OnDestroy manually. Unity executes it once
            // when the component is destroyed.
            ToolbarControl doomed = toolbar;
            toolbar = null;
            if (doomed != null) Destroy(doomed);
            if (owner == this) owner = null;
        }

        private void OnDestroy()
        {
            UnbindLauncherEvents();
            onShow = null;
            onHide = null;
            toolbar = null;
            if (owner == this) owner = null;
        }
    }
}
