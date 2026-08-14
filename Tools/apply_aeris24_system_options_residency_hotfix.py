#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = "oh_gpu_vertex_resident_suspend="


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS24 SYSTEM OPTIONS/RESIDENCY] %s: expected 1 anchor, found %d" %
                         (label, count))
    return text.replace(old, new, 1)


settings_path = ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs"
window_path = ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs"
renderer_path = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
backend_path = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
config_path = ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg"

S = settings_path.read_text()
W = window_path.read_text()
R = renderer_path.read_text()
B = backend_path.read_text()
C = config_path.read_text()

if MARKER in R and "DrawProjectionBackendSelector" in W and \
   "GUILayout.HorizontalSlider" in W and "RetainForViewportSuspension" in B:
    print("[AERIS24 SYSTEM OPTIONS/RESIDENCY] already patched")
else:
    if "AERISNdProjectionBackendMode" not in S or "oh_nd_reload_snapshot=" not in R or \
       "RequestedModeName" not in B:
        raise SystemExit("[AERIS24 SYSTEM OPTIONS/RESIDENCY] rev005 predecessor absent")

    # SYSTEM > OPTIONS mirrors the ND-local projection selector. Both write the exact
    # same persisted setting; the renderer remains the single reload/backend authority.
    W = replace_once(W,
'''  static readonly string[] FlightArchiveLimitLabels=new string[]{"1","2","3","4","5","6","7","8","9","10","11","12","13","14","15","16","17","18","19","20","21","22","23","24","25","26","27","28","29","30"};\n''',
'''  // FDR archive retention uses one integer slider; the former 30-button grid is intentionally removed.\n''',
'archive label grid removal')

    W = replace_once(W,
'''   DrawDisplayModeSelector("ND",ref settings.NavigationDisplayMode);\n   ToggleOption(ref settings.NavigationDisplayTrackUp,"Navigation display track-up");''',
'''   DrawDisplayModeSelector("ND",ref settings.NavigationDisplayMode);\n   DrawProjectionBackendSelector();\n   ToggleOption(ref settings.NavigationDisplayTrackUp,"Navigation display track-up");''',
'SYSTEM projection selector placement')

    W = replace_once(W,
'''   DrawTerrainModeSelector();\n   DrawTerrainGpuSelector();\n   DrawTerrainColourSelector();''',
'''   DrawTerrainModeSelector();\n   DrawTerrainColourSelector();''',
'remove Terrain GPU options row')

    old_gpu_method = '''  void DrawTerrainGpuSelector(){GUILayout.BeginHorizontal();GUILayout.Label("Terrain GPU",GUILayout.Width(150f));int selected=GUILayout.SelectionGrid((int)settings.TerrainGpuMode,new string[]{"AUTO","ON","OFF"},3,GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),GUILayout.Height(CompactControlHeight()));GUILayout.EndHorizontal();var next=(AERISTerrainGpuMode)Mathf.Clamp(selected,0,2);if(next!=settings.TerrainGpuMode){settings.TerrainGpuMode=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Terrain GPU="+next);}}'''
    new_projection_method = '''  void DrawProjectionBackendSelector()\n  {\n   GUILayout.BeginHorizontal();\n   GUILayout.Label("ND projection",GUILayout.Width(150f));\n   int selected=GUILayout.SelectionGrid((int)settings.NavigationDisplayProjectionBackend,\n    new string[]{"AUTO","CPU","GPU"},3,GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),\n    GUILayout.Height(CompactControlHeight()));\n   GUILayout.EndHorizontal();\n   var next=(AERISNdProjectionBackendMode)Mathf.Clamp(selected,0,2);\n   if(next!=settings.NavigationDisplayProjectionBackend)\n   {\n    settings.NavigationDisplayProjectionBackend=next;\n    settings.Save();\n    AERISLogger.Info("[SYSTEM/OPTIONS] ND projection backend="+next);\n   }\n  }'''
    W = replace_once(W, old_gpu_method, new_projection_method,
                     'replace Terrain GPU method with projection method')

    old_archive = '''  void DrawFlightDataArchiveLimitSelector(){GUILayout.Label("Verified flight ZIP limit   (default 10 / range 1-30)");int current=AERISSettings.NormalizeFlightDataArchiveLimit(settings.FlightDataArchiveLimit)-1;int selected=GUILayout.SelectionGrid(current,FlightArchiveLimitLabels,10,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()*3f));if(selected>=0&&selected<30){int next=selected+1;if(next!=settings.FlightDataArchiveLimit){settings.FlightDataArchiveLimit=next;settings.Save();AERISFlightDataArchive.ConfigureRetention(next);AERISLogger.Info("[SYSTEM/OPTIONS] FDR/CVR verified ZIP limit="+next);}}}'''
    new_archive = '''  void DrawFlightDataArchiveLimitSelector()\n  {\n   int current=AERISSettings.NormalizeFlightDataArchiveLimit(settings.FlightDataArchiveLimit);\n   GUILayout.Label("Verified flight ZIP limit   "+current+"   (default 10 / range 1-30)");\n   GUILayout.BeginHorizontal();\n   GUILayout.Label("1",GUILayout.Width(20f));\n   float raw=GUILayout.HorizontalSlider(current,1f,30f,GUILayout.ExpandWidth(true));\n   GUILayout.Label("30",GUILayout.Width(28f));\n   GUILayout.EndHorizontal();\n   int next=Mathf.Clamp(Mathf.RoundToInt(raw),1,30);\n   if(next!=settings.FlightDataArchiveLimit)\n   {\n    settings.FlightDataArchiveLimit=next;\n    settings.Save();\n    AERISFlightDataArchive.ConfigureRetention(next);\n    AERISLogger.Info("[SYSTEM/OPTIONS] FDR/CVR verified ZIP limit="+next);\n   }\n  }'''
    W = replace_once(W, old_archive, new_archive, 'FDR archive integer slider')

    # Terrain GPU presentation is no longer user-selectable. Keep the legacy CFG key
    # readable/writable for compatibility, but normalize every runtime to ON.
    S = replace_once(S,
'''        internal AERISTerrainGpuMode TerrainGpuMode = AERISTerrainGpuMode.Automatic;''',
'''        internal AERISTerrainGpuMode TerrainGpuMode = AERISTerrainGpuMode.On;''',
'Terrain GPU default ON')
    S = replace_once(S,
'''                settings.TerrainGpuMode = ReadEnum(node, "terrainGpuMode",\n                    AERISTerrainGpuMode.Automatic);''',
'''                // AERIS24: terrain GPU presentation is a fixed platform policy.\n                // Legacy AUTO/OFF values are intentionally migrated to ON.\n                settings.TerrainGpuMode = AERISTerrainGpuMode.On;''',
'Terrain GPU load normalization')
    S = replace_once(S,
'''            TerrainGpuMode = AERISTerrainGpuMode.Automatic;''',
'''            TerrainGpuMode = AERISTerrainGpuMode.On;''',
'Terrain GPU reset ON')
    S = replace_once(S,
'''                node.AddValue("terrainGpuMode", TerrainGpuMode);''',
'''                TerrainGpuMode = AERISTerrainGpuMode.On;\n                node.AddValue("terrainGpuMode", TerrainGpuMode);''',
'Terrain GPU save normalization')
    C = replace_once(C, '    terrainGpuMode = Automatic\n', '    terrainGpuMode = On\n',
                     'packaged Terrain GPU ON')

    # Visibility/viewport suspension releases large ND surfaces and terrain meshes, but
    # must not unload the tiny, already validated shader AssetBundle/material backend.
    # Backend switches still call ReleaseForSuspension(), and Dispose still fully releases.
    R = replace_once(R,
'''            uniformColourScratch.Clear();\n            gpuVertexGeographicScratch.Clear();\n            gpuVertexProjection.ReleaseForSuspension();\n            completed.Clear();''',
'''            uniformColourScratch.Clear();\n            gpuVertexGeographicScratch.Clear();\n            gpuVertexProjection.RetainForViewportSuspension();\n            completed.Clear();''',
'viewport keeps GPU projection backend resident')

    B = replace_once(B,
'''        string bundleLoadMode = "NONE";\n        AERISNdProjectionBackendMode requestedMode =\n            AERISNdProjectionBackendMode.Automatic;''',
'''        string bundleLoadMode = "NONE";\n        AERISNdProjectionBackendMode requestedMode =\n            AERISNdProjectionBackendMode.Automatic;\n        bool viewportSuspendedResident;\n        int activationCount;\n        int residentSuspensionCount;''',
'backend residency counters')

    B = replace_once(B,
'''        internal string Failure { get { return failure; } }\n        internal string BundlePath { get { return bundlePath; } }''',
'''        internal string Failure { get { return failure; } }\n        internal string BundlePath { get { return bundlePath; } }\n        internal int ActivationCount { get { return activationCount; } }\n        internal int ResidentSuspensionCount { get { return residentSuspensionCount; } }''',
'backend residency properties')

    B = replace_once(B,
'''        internal bool TryEnsureLoaded()\n        {\n            // Explicit CPU is a hard no-touch rail:''',
'''        internal bool TryEnsureLoaded()\n        {\n            // Resume is intentionally allocation-free when visibility alone suspended ND.\n            viewportSuspendedResident = false;\n            // Explicit CPU is a hard no-touch rail:''',
'resume resident backend')

    B = replace_once(B,
'''                failure = string.Empty;\n                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested=" +''',
'''                failure = string.Empty;\n                activationCount++;\n                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested=" +''',
'count real bundle activation')

    B = replace_once(B,
'''        internal void DisableAndFallback(string reason)\n        {''',
'''        internal void RetainForViewportSuspension()\n        {\n            if (viewportSuspendedResident) return;\n            viewportSuspendedResident = true;\n            if (!Active) return;\n            residentSuspensionCount++;\n            AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] RESIDENT SUSPEND; requested=" +\n                RequestedModeName + "; effective=" + EffectiveModeName +\n                "; activation=" + activationCount + "; retained=" +\n                residentSuspensionCount + ".");\n        }\n\n        internal void DisableAndFallback(string reason)\n        {''',
'resident viewport suspension API')

    B = replace_once(B,
'''            bundleLoadMode = "NONE";\n        }\n\n        public void Dispose()''',
'''            bundleLoadMode = "NONE";\n            viewportSuspendedResident = false;\n        }\n\n        public void Dispose()''',
'full release clears resident flag')

    R = replace_once(R,
'''                "; oh_gpu_vertex_backend_switch=" + operationHealthProjectionBackendSwitches +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +''',
'''                "; oh_gpu_vertex_backend_switch=" + operationHealthProjectionBackendSwitches +\n                "; oh_gpu_vertex_activation=" + gpuVertexProjection.ActivationCount +\n                "; oh_gpu_vertex_resident_suspend=" + gpuVertexProjection.ResidentSuspensionCount +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +''',
'GPU residency telemetry')

    settings_path.write_text(S)
    window_path.write_text(W)
    renderer_path.write_text(R)
    backend_path.write_text(B)
    config_path.write_text(C)

verifier = ROOT / "Tools/verify_aeris24_system_options_residency_hotfix.py"
if verifier.is_file():
    subprocess.run([sys.executable, str(verifier)], cwd=str(ROOT), check=True)
print("[AERIS24 SYSTEM OPTIONS/RESIDENCY] PASS")
