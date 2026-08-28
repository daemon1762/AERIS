#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
S = ROOT / 'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 SALBUTAMOL SULFATE R010 VERIFY]'
R009 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'


def ck(ok, label):
    if not ok:
        raise SystemExit(PREFIX + ' FAIL: ' + label)
    print(PREFIX + ' PASS: ' + label)


for p in (R, Z, S, B):
    ck(p.is_file(), 'file ' + str(p.relative_to(ROOT)))
r = R.read_text(); z = Z.read_text(); s = S.read_text(); b = B.read_text()
r_flat = ' '.join(r.split())

ck(R009 in r and R009 in z and R009 in s, 'R009 generated parent retained')
ck(R010 in r, 'R010 renderer marker')
ck('REV3_5_R010_VARIANT="' + R010 + '"' in b, 'R010 build identity')
ck('rev3_5_r010_variant=%s' in b, 'R010 candidate identity emission')
ck('verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py' in b,
   'R010 verifier wired into build')

# Single serial authority remains exactly one scalar pending commit.
ck(r.count('PendingEntryCommit pendingEntryCommit;') == 1,
   'single PendingEntryCommit field retained')
ck('List<PendingEntryCommit>' not in r and 'Queue<PendingEntryCommit>' not in r and
   'PendingEntryCommit[]' not in r,
   'no multi-pending lane introduced')

# Non-authoritative Repaint wakes on the accepted current-FAR queue authority. Legacy R010
# used only R007; R019 adds a visible-priority queue but keeps the same single staged lane.
legacy_wake = re.search(
    r'if \(pendingEntryCommit != null \|\| rasterizer\.CompletedCount > 0 \|\|\s*'
    r'rev35R007FoundationQueue\.Count > 0\)', r, re.S)
accepted_r019_wake = re.search(
    r'if \(pendingEntryCommit != null \|\| rasterizer\.CompletedCount > 0 \|\|\s*'
    r'rev35R019VisibleFoundationQueue\.Count > 0 \|\|\s*'
    r'rev35R007FoundationQueue\.Count > 0\)', r, re.S)
ck(legacy_wake is not None or accepted_r019_wake is not None,
   'current-FAR FIFO authority wakes non-authoritative staged pump')

# Adaptive budget and final backlog account the real queue authority. R019 splits visible
# priority from hidden FAR while preserving the inherited 128 combined queue ceiling.
legacy_queue_backlog = (
    'int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);' in r
)
accepted_r019_queue_backlog = (
    'int r010QueueBacklog = Math.Max(0, rev35R019VisibleFoundationQueue.Count) + Math.Max(0, rev35R007FoundationQueue.Count);' in r_flat
)
ck(legacy_queue_backlog or accepted_r019_queue_backlog,
   'accepted current-FAR queues included in adaptive backlog')
ck('(pendingEntryCommit == null ? 0 : 1) + r010QueueBacklog;' in r,
   'R004 backlog includes R010 queue term')
legacy_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) +\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r
)
accepted_r019_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) + Math.Max(0, rev35R019VisibleFoundationQueue.Count) + Math.Max(0, rev35R007FoundationQueue.Count);' in r_flat
)
ck(legacy_final_backlog or accepted_r019_final_backlog,
   'main commit final backlog includes accepted current-FAR queues')

# Existing safety rails remain exact.
ck('const double Rev35R004BudgetMaximumMilliseconds = 2.00;' in r,
   'R004 2.00ms ceiling retained')
ck('const int Rev35R005SourceChunkHardCap = 64;' in r,
   'R005 source hard cap 64 retained')
ck('const int Rev35R007FoundationQueueMaximum = 128;' in r,
   'R007 queue hard cap retained')
ck('presentationNow + 0.10f' in r, 'fixed 10Hz presentation cadence retained')
ck('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
   'ARGB32/Bilinear visual contract retained')
legacy_r010_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r
)
accepted_r018_foundation_gate = (
    'bool r018VisibleGpuComplete = operationHealthRev35R018VisiblePlanValid && operationHealthRev35R018VisibleRequiredFar > 0 && operationHealthRev35R018VisibleReadyFar >= operationHealthRev35R018VisibleRequiredFar;' in r_flat and
    'bool r018OverscanGpuComplete = visible.FoundationComplete && lastBackFoundationCoverage >= 0.999f && readyFar >= visible.FarFoundationCount;' in r_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r_flat and
    'if (!r018OverscanGpuComplete) operationHealthRev35R018OverscanHolAvoided++;' in r_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete && r018OverscanGpuComplete' not in r_flat
)
ck(legacy_r010_foundation_gate or accepted_r018_foundation_gate,
   'foundation swap gate is legacy R010 strict coverage or exact accepted R018 visible-GPU descendant')

# R009 remains intact; R010 does not touch scheduler/rasterizer semantics.
ck('SubmitRequired(AERISRuntimeLane.GeneralCompute' in z,
   'R009 required terrain admission retained')
ck('Rev35R009QueueProtection' in s,
   'R009 required queue protection retained')

for forbidden in (
    'Task.Run(', 'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    ck(forbidden not in r and forbidden not in z, 'rejected mechanism absent: ' + forbidden)

for witness in (
    'oh_rev35_r010_variant=',
    'oh_rev35_r010_queue_budget_samples=',
    'oh_rev35_r010_queue_backlog_peak='):
    ck(witness in r, 'telemetry ' + witness)
ck('oh_rev35_r010_queue_kick=' not in r,
   'obsolete diagnostic queue-kick telemetry absent')

print(PREFIX + ' ALL PASS')
print('contract=single serial commit lane + accepted current-FAR queue wake + real backlog budgeting')
print('worker/scheduler/rasterizer semantics=R009 retained; quality/10Hz/range unchanged')
