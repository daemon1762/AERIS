#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
O = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 R007 MANAGED HEAP ATTRIBUTION VERIFY]'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_MANAGED_HEAP_ATTRIBUTION'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'


def fail(label):
    print('[FAIL] ' + label)
    raise SystemExit(1)


def passed(label):
    print('[PASS] ' + label)


for path in (R, T, O, B):
    if not path.is_file():
        fail('required file exists: ' + str(path))
r = R.read_text()
t = T.read_text()
o = O.read_text()
b = B.read_text()

checks = [
    (R007 in r and R007 in t and R007 in o, 'R007 marker in renderer/tile/OH'),
    (HF3 in t, 'HF3 monotonic complete-coverage parent retained'),
    ('BeginRev35R007HeapWindow()' in r and
     'ObserveRev35R007HeapWindow(' in r, 'renderer heap-window observer present'),
    ('oh_rev35_r007_drain_pos_bytes=' in r and
     'oh_rev35_r007_capture_pos_bytes=' in r and
     'oh_rev35_r007_resolve_pos_bytes=' in r and
     'oh_rev35_r007_prune_pos_bytes=' in r, 'renderer stage telemetry present'),
    ('r007_preload_point_pos_bytes=' in t and
     'r007_plan_pos_bytes=' in t and
     'r007_io_pos_bytes=' in t and
     'r007_schedule_pos_bytes=' in t, 'tile-system stage telemetry present'),
    ('rev35R007NextMaintenanceHeapSampleRealtime = now + 0.50f;' in t,
     'always-on maintenance heap sampling bounded to 2 Hz'),
    ('[R007_GC]' in o and 'forced_gc=0' in o and
     'rev35R007Gen2IntervalMinMs' in o and
     'rev35R007Gen2IntervalMaxMs' in o, 'passive Gen2 interval witness present'),
    ('GC.GetTotalMemory(false)' in r and
     'GC.GetTotalMemory(false)' in t and
     'GC.GetTotalMemory(false)' in o, 'heap observations use non-forcing API'),
    ('GC.Collect(' not in r and 'GC.Collect(' not in t and 'GC.Collect(' not in o,
     'R007 never forces GC'),
    ('UnloadUnusedAssets' not in r and 'UnloadUnusedAssets' not in t,
     'R007 does not force Unity unload'),
    ('presentationNow + 0.10f' in r, 'fixed 10 Hz authority retained'),
    ('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
     'ARGB32/Bilinear visual contract retained'),
    ('rev3_5_r007_variant=' in b and
     'verify_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py' in b,
     'build identity/verifier chain present'),
    ('Task.Run(' not in r and 'new Thread(' not in r and 'ThreadPool.' not in r,
     'no new worker/thread mechanism'),
    ('WaitManagedPreparation' not in r and 'ResidentPreparedPresentation' not in r and
     'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE' not in r,
     'rejected mechanisms remain absent'),
]
for ok, label in checks:
    if not ok:
        fail(label)
    passed(label)

# Ensure attribution ordering is really around the intended calls rather than merely present.
drain = r.find('DrainCompleted(system);')
drain_obs = r.find('ref operationHealthRev35R007DrainSamples', drain)
capture = r.find('visible = system.CaptureVisible(', drain)
capture_obs = r.find('ref operationHealthRev35R007CaptureSamples', capture)
foundation = r.find('ObserveRev35R006FoundationCriticalPath(', capture)
resolve_obs = r.find('ref operationHealthRev35R007ResolveSamples', foundation)
prune = r.find('Prune(ResolveVramLimitBytes());')
prune_obs = r.find('ref operationHealthRev35R007PruneSamples', prune)
if not (0 <= drain < drain_obs < capture < capture_obs < foundation < resolve_obs):
    fail('renderer attribution ordering')
passed('renderer attribution ordering')
if not (0 <= prune < prune_obs):
    fail('prune attribution ordering')
passed('prune attribution ordering')

print(PREFIX + ' STATIC PASS')
