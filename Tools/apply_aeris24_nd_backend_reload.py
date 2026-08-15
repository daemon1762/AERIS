#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = "AERIS24 ND BACKEND/BLACK RELOAD"


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS24 ND BACKEND RELOAD] %s: expected 1 anchor, found %d" %
                         (label, count))
    return text.replace(old, new, 1)


def patch_settings():
    path = ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs"
    text = path.read_text()
    if "AERISNdProjectionBackendMode" in text:
        print("[AERIS24 ND BACKEND RELOAD] settings already patched")
        return
    text = replace_once(text,
'''    internal enum AERISNavigationDisplayUpdateMode
    {
        Automatic = 0,
        Fps10 = 1,
        Fps20 = 2,
        Fps30 = 3,
        Fps45 = 4,
        Fps60 = 5
    }
''',
'''    internal enum AERISNavigationDisplayUpdateMode
    {
        Automatic = 0,
        Fps10 = 1,
        Fps20 = 2,
        Fps30 = 3,
        Fps45 = 4,
        Fps60 = 5
    }

    // AERIS24 ND BACKEND/BLACK RELOAD. This selects only the geographic vertex
    // projection implementation. Terrain content, painter order and control/safety
    // authority remain unchanged.
    internal enum AERISNdProjectionBackendMode
    {
        Automatic = 0,
        Cpu = 1,
        Gpu = 2
    }
''', 'backend enum')
    text = replace_once(text,
'''        internal AERISNavigationDisplayUpdateMode NavigationDisplayUpdateMode =
            AERISNavigationDisplayUpdateMode.Fps10;
        internal AERISTerrainDisplayMode TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;''',
'''        internal AERISNavigationDisplayUpdateMode NavigationDisplayUpdateMode =
            AERISNavigationDisplayUpdateMode.Fps10;
        internal AERISNdProjectionBackendMode NavigationDisplayProjectionBackend =
            AERISNdProjectionBackendMode.Automatic;
        internal AERISTerrainDisplayMode TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;''',
'backend setting field')
    text = replace_once(text,
'''                settings.NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
                if (terrainQualityRevision != CurrentTerrainQualityModelRevision ||''',
'''                settings.NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
                settings.NavigationDisplayProjectionBackend = ReadEnum(node,
                    "navigationDisplayProjectionBackend",
                    AERISNdProjectionBackendMode.Automatic);
                if (terrainQualityRevision != CurrentTerrainQualityModelRevision ||''',
'backend load')
    text = replace_once(text,
'''            NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
            TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;''',
'''            NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
            NavigationDisplayProjectionBackend = AERISNdProjectionBackendMode.Automatic;
            TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;''',
'backend reset default')
    text = replace_once(text,
'''                node.AddValue("navigationDisplayUpdateMode", NavigationDisplayUpdateMode);
                node.AddValue("terrainDisplayMode", TerrainDisplayMode);''',
'''                node.AddValue("navigationDisplayUpdateMode", NavigationDisplayUpdateMode);
                node.AddValue("navigationDisplayProjectionBackend",
                    NavigationDisplayProjectionBackend);
                node.AddValue("terrainDisplayMode", TerrainDisplayMode);''',
'backend save')
    path.write_text(text)


def patch_backend():
    path = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
    text = path.read_text()
    if "RequestedModeName" in text and "CPU_EXACT_REQUESTED" in text:
        print("[AERIS24 ND BACKEND RELOAD] backend already patched")
        return
    text = replace_once(text,
'''using UnityEngine;
using AERISFlightControl.Logging;''',
'''using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;''',
'backend settings using')
    text = replace_once(text,
'''        string bundlePath = string.Empty;
        string bundleLoadMode = "NONE";
''',
'''        string bundlePath = string.Empty;
        string bundleLoadMode = "NONE";
        AERISNdProjectionBackendMode requestedMode =
            AERISNdProjectionBackendMode.Automatic;
''',
'backend requested mode state')
    text = replace_once(text,
'''        internal string Failure { get { return failure; } }
        internal string BundlePath { get { return bundlePath; } }
        internal Material TerrainMaterial { get { return terrainMaterial; } }''',
'''        internal string Failure { get { return failure; } }
        internal string BundlePath { get { return bundlePath; } }
        internal string RequestedModeName
        {
            get
            {
                switch (requestedMode)
                {
                    case AERISNdProjectionBackendMode.Cpu: return "CPU";
                    case AERISNdProjectionBackendMode.Gpu: return "GPU";
                    default: return "AUTO";
                }
            }
        }
        internal string EffectiveModeName
        {
            get
            {
                if (requestedMode == AERISNdProjectionBackendMode.Cpu)
                    return "CPU_EXACT";
                if (Active) return "GPU_ACTIVE";
                return attempted ? "CPU_FALLBACK" : "GPU_PENDING";
            }
        }
        internal Material TerrainMaterial { get { return terrainMaterial; } }''',
'backend requested/effective properties')
    text = replace_once(text,
'''        internal bool TryEnsureLoaded()
        {
            if (attempted) return Active;
            attempted = true;
            try''',
'''        internal void SetRequestedMode(AERISNdProjectionBackendMode mode)
        {
            if (mode == requestedMode) return;
            ReleaseForSuspension();
            requestedMode = mode;
            AERISLogger.Info("[AERIS24_ND_PROJECTION_BACKEND] requested=" +
                RequestedModeName + "; effective=" + EffectiveModeName + ".");
        }

        internal bool TryEnsureLoaded()
        {
            // Explicit CPU is a hard no-touch rail: do not probe, read or invoke
            // AssetBundle APIs at all. AUTO/GPU retain the same fail-closed GPU attempt.
            if (requestedMode == AERISNdProjectionBackendMode.Cpu)
            {
                if (!attempted)
                {
                    attempted = true;
                    disabled = true;
                    failure = "CPU_EXACT_REQUESTED";
                    AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] SKIPPED; " +
                        "requested=CPU; effective=CPU_EXACT; AssetBundleInit=0.");
                }
                return false;
            }
            if (attempted) return Active;
            attempted = true;
            try''',
'backend CPU no-touch gate')
    text = replace_once(text,
'''                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=" +
                    ShaderName + "; bundle=" + fileName + "; load=" + bundleLoadMode +''',
'''                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested=" +
                    RequestedModeName + "; effective=" + EffectiveModeName + "; shader=" +
                    ShaderName + "; bundle=" + fileName + "; load=" + bundleLoadMode +''',
'backend active telemetry')
    text = replace_once(text,
'''            AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] CPU EXACT FALLBACK; reason=" +
                failure + "; unity=" + Application.unityVersion + "; platform=" +''',
'''            AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] CPU EXACT FALLBACK; requested=" +
                RequestedModeName + "; effective=" + EffectiveModeName + "; reason=" +
                failure + "; unity=" + Application.unityVersion + "; platform=" +''',
'backend fallback telemetry')
    path.write_text(text)


def patch_renderer():
    path = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
    text = path.read_text()
    if "ReloadProgressPercent" in text and "oh_gpu_vertex_requested=" in text:
        print("[AERIS24 ND BACKEND RELOAD] renderer already patched")
        return
    if "oh_gpu_vertex_projection=" not in text or "gpuVertexProjectionBackFailure" not in text:
        raise SystemExit("[AERIS24 ND BACKEND RELOAD] AERIS24 GPU predecessor not generated")
    text = replace_once(text,
'''        bool gpuVertexProjectionBackFailure;
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',
'''        bool gpuVertexProjectionBackFailure;
        AERISNdProjectionBackendMode projectionBackendMode =
            (AERISNdProjectionBackendMode)(-1);
        long ndReloadGeneration = 1L;
        long frontReloadGeneration;
        long operationHealthProjectionBackendSwitches;
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',
'renderer backend/reload fields')
    text = replace_once(text,
'''        internal bool RequestedViewReady { get { return requestedViewReady; } }
        internal float LastRunwayMapLockErrorPixels''',
'''        internal bool RequestedViewReady { get { return requestedViewReady; } }
        internal bool Reloading
        {
            get { return !requestedViewReady || frontReloadGeneration != ndReloadGeneration; }
        }
        internal int ReloadProgressPercent
        {
            get
            {
                if (!Reloading) return 100;
                return Mathf.Clamp(Mathf.RoundToInt(
                    Mathf.Clamp01(lastBackFoundationCoverage) * 100f), 0, 99);
            }
        }
        internal string ProjectionBackendRequested
        {
            get { return gpuVertexProjection.RequestedModeName; }
        }
        internal string ProjectionBackendEffective
        {
            get { return gpuVertexProjection.EffectiveModeName; }
        }
        internal float LastRunwayMapLockErrorPixels''',
'renderer public reload/backend state')
    text = replace_once(text,
'''            operationHealthViewInvalidations++;
            generation++;
            rasterizer.CancelAll();''',
'''            operationHealthViewInvalidations++;
            generation++;
            ndReloadGeneration++;
            rasterizer.CancelAll();''',
'reload generation on view invalidation')
    text = replace_once(text,
'''            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||''',
'''            AERISNdProjectionBackendMode requestedProjectionBackend = settings == null ?
                AERISNdProjectionBackendMode.Automatic :
                settings.NavigationDisplayProjectionBackend;
            if (requestedProjectionBackend != projectionBackendMode)
            {
                projectionBackendMode = requestedProjectionBackend;
                gpuVertexProjection.SetRequestedMode(requestedProjectionBackend);
                operationHealthProjectionBackendSwitches++;
                if (frontBufferValid || requestedViewReady || contentSnapshotValid)
                    InvalidatePendingForViewChange();
                else requestedViewReady = false;
                AERISLogger.Info("[AERIS24_ND_PROJECTION_BACKEND] requested=" +
                    gpuVertexProjection.RequestedModeName + "; effective=" +
                    gpuVertexProjection.EffectiveModeName + "; reloadGeneration=" +
                    ndReloadGeneration + ".");
            }

            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||''',
'renderer backend switch synchronization')
    text = replace_once(text,
'''            if (!authoritativeTickDue)
            {
                if (TryPresentCoalescedFront(plot, vessel))
                    return lastDrawState;
                if (!frontBufferValid)''',
'''            if (!authoritativeTickDue)
            {
                if (Reloading)
                {
                    operationHealthCoalescedBlankPolls++;
                    lastFrontBufferPresented = false;
                    presentedProjection.Valid = false;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    return lastDrawState;
                }
                if (TryPresentCoalescedFront(plot, vessel))
                    return lastDrawState;
                if (!frontBufferValid)''',
'black reload retained-front gate')
    text = replace_once(text,
'''            if (directCompatible)
            {
                PresentFrontDirect(plot, frontOrientation);''',
'''            if (!Reloading && directCompatible)
            {
                PresentFrontDirect(plot, frontOrientation);''',
'block stale direct FRONT while reloading')
    text = replace_once(text,
'''            if (!present && colourCompatible &&
                CanPresentLatchedFront(visible, vessel))''',
'''            if (!present && !Reloading && colourCompatible &&
                CanPresentLatchedFront(visible, vessel))''',
'block latched FRONT while reloading')
    text = replace_once(text,
'''            requestedViewReady = true;
            if (gpuContentDirty) operationHealthDirtyCommits++;''',
'''            frontReloadGeneration = ndReloadGeneration;
            requestedViewReady = true;
            if (gpuContentDirty) operationHealthDirtyCommits++;''',
'fresh FRONT closes reload generation')
    text = replace_once(text,
'''        bool TryPresentCoalescedFront(Rect plot, Vessel vessel)
        {
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||''',
'''        bool TryPresentCoalescedFront(Rect plot, Vessel vessel)
        {
            if (Reloading) return false;
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||''',
'coalesced stale FRONT hard block')
    text = replace_once(text,
'''                "; oh_gpu_vertex_projection=" +
                    (gpuVertexProjection.Active ? "ACTIVE" : "CPU_FALLBACK") +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +''',
'''                "; oh_gpu_vertex_requested=" + gpuVertexProjection.RequestedModeName +
                "; oh_gpu_vertex_projection=" + gpuVertexProjection.EffectiveModeName +
                "; oh_gpu_vertex_backend_switch=" + operationHealthProjectionBackendSwitches +
                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +
                "; oh_nd_reload_pct=" + ReloadProgressPercent +
                "; oh_nd_reload_generation=" + ndReloadGeneration +
                "; oh_nd_front_reload_generation=" + frontReloadGeneration +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +''',
'backend/reload telemetry')
    path.write_text(text)


def patch_ui():
    path = ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
    text = path.read_text()
    if "FormatProjectionBackend" in text and "RELOADING ND" in text:
        print("[AERIS24 ND BACKEND RELOAD] UI already patched")
        return
    text = replace_once(text,
'''                else DrawCleanBackground(plan);

                // Gate 5 Candidate 2 map-authority latch.''',
'''                else DrawCleanBackground(plan);

                bool ndReloading = (planMode || !landActive || overlay) &&
                    terrainTileRenderer != null && terrainTileRenderer.Reloading &&
                    terrainTileRenderer.LastDrawState == AERISTerrainGpuDrawState.Partial;
                if (ndReloading)
                {
                    DrawCleanBackground(plan);
                    if (landActive && profile.width > 0f && profile.height > 0f)
                        DrawCleanBackground(profile);
                    DrawLabel(plan, "RELOADING ND\\n" +
                        terrainTileRenderer.ReloadProgressPercent + "%", centerStyle,
                        new Color(0.72f, 0.86f, 0.92f, 1f));
                    // During reload no map, ownship, runway, traffic, trail, vector,
                    // wind or LAND symbology may survive over the black viewport.
                    DrawMapControls(mapControlsRect, landActive, requestedRange, overlay,
                        planMode, scale);
                    if (core.Performance != null)
                        core.Performance.RecordNavigationDisplayState(planMode, requestedRange);
                    return;
                }

                // Gate 5 Candidate 2 map-authority latch.''',
'black reload UI gate')
    text = replace_once(text,
'''            if (gpuState == AERISTerrainGpuDrawState.Partial)
            {
                int percent = Mathf.Clamp(Mathf.RoundToInt(
                    (terrainTileRenderer == null ? 0f :
                    terrainTileRenderer.LastBackFoundationCoverage) * 100f), 0, 99);
                DrawLabel(plot, "TERRAIN GPU BUILDING " + percent + "%", centerStyle,
                    new Color(0.58f, 0.76f, 0.82f, 1f));
                return;
            }''',
'''            if (gpuState == AERISTerrainGpuDrawState.Partial)
            {
                if (terrainTileRenderer != null && terrainTileRenderer.Reloading)
                {
                    DrawCleanBackground(plot);
                    DrawLabel(plot, "RELOADING ND\\n" +
                        terrainTileRenderer.ReloadProgressPercent + "%", centerStyle,
                        new Color(0.72f, 0.86f, 0.92f, 1f));
                }
                else
                {
                    int percent = Mathf.Clamp(Mathf.RoundToInt(
                        (terrainTileRenderer == null ? 0f :
                        terrainTileRenderer.LastBackFoundationCoverage) * 100f), 0, 99);
                    DrawLabel(plot, "TERRAIN GPU BUILDING " + percent + "%", centerStyle,
                        new Color(0.58f, 0.76f, 0.82f, 1f));
                }
                return;
            }''',
'reload building label')
    text = replace_once(text,
'''        string FormatTerrainRenderTargetOrientation()
        {
            return settings != null && settings.TerrainRenderTargetOrientation ==''',
'''        string FormatProjectionBackend()
        {
            if (settings == null) return "PROJ AUTO";
            switch (settings.NavigationDisplayProjectionBackend)
            {
                case AERISNdProjectionBackendMode.Cpu: return "PROJ CPU";
                case AERISNdProjectionBackendMode.Gpu: return "PROJ GPU";
                default: return "PROJ AUTO";
            }
        }

        void CycleProjectionBackend()
        {
            if (settings == null) return;
            switch (settings.NavigationDisplayProjectionBackend)
            {
                case AERISNdProjectionBackendMode.Automatic:
                    settings.NavigationDisplayProjectionBackend =
                        AERISNdProjectionBackendMode.Cpu; break;
                case AERISNdProjectionBackendMode.Cpu:
                    settings.NavigationDisplayProjectionBackend =
                        AERISNdProjectionBackendMode.Gpu; break;
                default:
                    settings.NavigationDisplayProjectionBackend =
                        AERISNdProjectionBackendMode.Automatic; break;
            }
            AERISLogger.Info("[ND/PROJECTION_BACKEND] requested=" +
                settings.NavigationDisplayProjectionBackend + ".");
        }

        string FormatTerrainRenderTargetOrientation()
        {
            return settings != null && settings.TerrainRenderTargetOrientation ==''',
'projection backend UI helpers')
    text = replace_once(text,
'''            int rows = 4 + (windAvailable ? 1 : 0) + (landActive ? 2 : 0);''',
'''            int rows = 5 + (windAvailable ? 1 : 0) + (landActive ? 2 : 0);''',
'aux menu row count')
    text = replace_once(text,
'''                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    FormatTerrainRenderTargetOrientation(), buttonStyle))
                { CycleTerrainRenderTargetOrientation(); changed = true; }
                y += rowHeight;
                bool windAvailable = !string.IsNullOrEmpty(AERISWindProviderApi.ProviderName);''',
'''                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    FormatTerrainRenderTargetOrientation(), buttonStyle))
                { CycleTerrainRenderTargetOrientation(); changed = true; }
                y += rowHeight;
                GUI.enabled = true;
                GUI.backgroundColor = new Color(0.30f, 0.42f, 0.48f, 1f);
                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    FormatProjectionBackend(), buttonStyle))
                { CycleProjectionBackend(); changed = true; }
                y += rowHeight;
                bool windAvailable = !string.IsNullOrEmpty(AERISWindProviderApi.ProviderName);''',
'projection backend menu row')
    # Discrete view/presentation mode changes are explicit reloads. Aircraft-motion
    # refreshes are deliberately NOT routed here, preserving the normal 10 Hz moving map.
    text = replace_once(text,
'''            AERISLogger.Info("[ND/TERRAIN] display mode=" +
                settings.TerrainDisplayMode);''',
'''            if (terrainTileRenderer != null)
                terrainTileRenderer.InvalidatePendingForViewChange();
            AERISLogger.Info("[ND/TERRAIN] display mode=" +
                settings.TerrainDisplayMode);''',
'terrain mode reload invalidation')
    text = replace_once(text,
'''                    settings.NavigationDisplayTrackUp = !settings.NavigationDisplayTrackUp;
                    SaveSettingsAndProfile();''',
'''                    settings.NavigationDisplayTrackUp = !settings.NavigationDisplayTrackUp;
                    if (terrainTileRenderer != null)
                        terrainTileRenderer.InvalidatePendingForViewChange();
                    SaveSettingsAndProfile();''',
'track-up reload invalidation')
    # Orientation is also a projection resource change, so it follows the same black reload rail.
    text = replace_once(text,
'''            if (terrainTileRenderer != null) terrainTileRenderer.ResetGpuFailure();
            AERISLogger.Info("[ND/TERRAIN_ALIGN] presentation orientation changed to " +''',
'''            if (terrainTileRenderer != null)
            {
                terrainTileRenderer.ResetGpuFailure();
                terrainTileRenderer.InvalidatePendingForViewChange();
            }
            AERISLogger.Info("[ND/TERRAIN_ALIGN] presentation orientation changed to " +''',
'orientation reload invalidation')
    path.write_text(text)


def main():
    patch_settings()
    patch_backend()
    patch_renderer()
    patch_ui()
    verifier = ROOT / "Tools/verify_aeris24_nd_backend_reload.py"
    if verifier.is_file():
        subprocess.run([sys.executable, str(verifier)], cwd=str(ROOT), check=True)
    print("[AERIS24 ND BACKEND RELOAD] PASS")


if __name__ == "__main__":
    main()
