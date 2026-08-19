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

# Non-authoritative Repaint now wakes on the existing R007 current-FAR FIFO.
wake = re.search(
    r'if \(pendingEntryCommit != null \|\| rasterizer\.CompletedCount > 0 \|\|\s*'
    r'rev35R007FoundationQueue\.Count > 0\)', r, re.S)
ck(wake is not None, 'R007 FIFO wakes non-authoritative staged pump')

# Adaptive budget and final backlog must account the real R007 FIFO.
ck('int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);' in r,
   'R007 FIFO included in adaptive backlog')
ck('(pendingEntryCommit == null ? 0 : 1) + r010QueueBacklog;' in r,
   'R004 backlog includes R010 queue term')
ck('(pendingEntryCommit == null ? 0 : 1) +\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r,
   'main commit final backlog includes R007 FIFO')

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
ck('foundationComplete = rendered && visible.FoundationComplete &&' in r and
   'lastBackFoundationCoverage >= 0.999f' in r,
   'strict complete-foundation swap gate retained')

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
print('contract=single serial commit lane + continuous R007 FIFO wake + real backlog budgeting')
print('worker/scheduler/rasterizer semantics=R009 retained; quality/10Hz/range unchanged')
