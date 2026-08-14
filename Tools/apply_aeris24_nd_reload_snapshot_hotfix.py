#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = "oh_nd_reload_snapshot="


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS24 ND RELOAD SNAPSHOT] %s: expected 1 anchor, found %d" %
                         (label, count))
    return text.replace(old, new, 1)


path = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
text = path.read_text()
if MARKER in text:
    print("[AERIS24 ND RELOAD SNAPSHOT] already patched")
else:
    if "ReloadProgressPercent" not in text or "ndReloadGeneration" not in text or \
       "oh_nd_reload_pct=" not in text:
        raise SystemExit("[AERIS24 ND RELOAD SNAPSHOT] backend/black reload predecessor absent")

    text = replace_once(text,
'''        long operationHealthProjectionBackendSwitches;
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',
'''        long operationHealthProjectionBackendSwitches;
        // Hotfix: while the ND is deliberately black for a discrete view/backend
        // reload, freeze the geographic request at one authoritative snapshot. At
        // high groundspeed a live center can otherwise outrun FAR generation and
        // make progress regress indefinitely. The lock is presentation-only and is
        // released immediately after the fresh FRONT for this reload generation.
        bool reloadSnapshotPending = true;
        bool reloadSnapshotActive;
        double reloadSnapshotCenterLatitudeDeg;
        double reloadSnapshotCenterLongitudeDeg;
        float reloadSnapshotMapHeadingDeg;
        int reloadProgressPercentFloor;
        long operationHealthReloadSnapshotCaptures;
        long operationHealthReloadSnapshotFrames;
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',
'reload snapshot fields')

    text = replace_once(text,
'''        internal int ReloadProgressPercent
        {
            get
            {
                if (!Reloading) return 100;
                return Mathf.Clamp(Mathf.RoundToInt(
                    Mathf.Clamp01(lastBackFoundationCoverage) * 100f), 0, 99);
            }
        }''',
'''        internal int ReloadProgressPercent
        {
            get
            {
                if (!Reloading) return 100;
                int measured = Mathf.Clamp(Mathf.RoundToInt(
                    Mathf.Clamp01(lastBackFoundationCoverage) * 100f), 0, 99);
                if (measured > reloadProgressPercentFloor)
                    reloadProgressPercentFloor = measured;
                return reloadProgressPercentFloor;
            }
        }''',
'monotonic reload progress')

    text = replace_once(text,
'''            ndReloadGeneration++;
            rasterizer.CancelAll();''',
'''            ndReloadGeneration++;
            reloadSnapshotPending = true;
            reloadSnapshotActive = false;
            reloadProgressPercentFloor = 0;
            rasterizer.CancelAll();''',
'reset snapshot on discrete invalidation')

    text = replace_once(text,
'''            }

            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||''',
'''            }

            // Freeze only deliberate black-reload construction. Normal moving-map
            // authority remains live after the fresh FRONT is committed. Range,
            // orientation, anchor and backend are the requested target state; center
            // and heading are the motion variables that must not chase the aircraft.
            if (Reloading)
            {
                if (reloadSnapshotPending || !reloadSnapshotActive)
                {
                    reloadSnapshotCenterLatitudeDeg = centerLatitudeDeg;
                    reloadSnapshotCenterLongitudeDeg = centerLongitudeDeg;
                    reloadSnapshotMapHeadingDeg = mapHeadingDeg;
                    reloadSnapshotPending = false;
                    reloadSnapshotActive = true;
                    reloadProgressPercentFloor = 0;
                    operationHealthReloadSnapshotCaptures++;
                    AERISLogger.Info("[AERIS24_ND_RELOAD_SNAPSHOT] generation=" +
                        ndReloadGeneration + "; center=" + centerLatitudeDeg + "," +
                        centerLongitudeDeg + "; heading=" + mapHeadingDeg + ".");
                }
                centerLatitudeDeg = reloadSnapshotCenterLatitudeDeg;
                centerLongitudeDeg = reloadSnapshotCenterLongitudeDeg;
                mapHeadingDeg = reloadSnapshotMapHeadingDeg;
                operationHealthReloadSnapshotFrames++;
            }

            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||''',
'freeze reload motion request')

    text = replace_once(text,
'''            frontReloadGeneration = ndReloadGeneration;
            requestedViewReady = true;
            if (gpuContentDirty) operationHealthDirtyCommits++;''',
'''            frontReloadGeneration = ndReloadGeneration;
            requestedViewReady = true;
            reloadSnapshotActive = false;
            reloadSnapshotPending = false;
            reloadProgressPercentFloor = 100;
            if (gpuContentDirty) operationHealthDirtyCommits++;''',
'release snapshot on exact FRONT commit')

    text = replace_once(text,
'''                "; oh_nd_front_reload_generation=" + frontReloadGeneration +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +''',
'''                "; oh_nd_front_reload_generation=" + frontReloadGeneration +
                "; oh_nd_reload_snapshot=" + (reloadSnapshotActive ? "LOCKED" : "LIVE") +
                "; oh_nd_reload_snapshot_capture=" + operationHealthReloadSnapshotCaptures +
                "; oh_nd_reload_snapshot_frames=" + operationHealthReloadSnapshotFrames +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +''',
'telemetry snapshot state')

    path.write_text(text)

verifier = ROOT / "Tools/verify_aeris24_nd_reload_snapshot_hotfix.py"
if verifier.is_file():
    subprocess.run([sys.executable, str(verifier)], cwd=str(ROOT), check=True)
print("[AERIS24 ND RELOAD SNAPSHOT] PASS")
