#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
M = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
C = ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg'
U = ROOT / 'build_ubuntu.sh'
P5V = ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'
STEP2 = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'

TAG = 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_001]'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1), True


def matching_brace(text, open_index):
    if open_index < 0 or open_index >= len(text) or text[open_index] != '{':
        raise ValueError('open brace required')
    depth = 0
    i = open_index
    state = 'code'
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return i
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    raise ValueError('unterminated brace')


renderer = R.read_text()
if 'AERIS25_PERSISTENT_PRESENTATION_BATCHING' not in renderer:
    raise SystemExit(PREFIX + ' accepted ADENOSINE parent is not generated')

const_old = '''        const float ContentPlanningHeadingStepDeg = 6f;\n'''
const_new = '''        const float ContentPlanningHeadingStepDeg = 6f;\n        // AERIS25_MAIN_THREAD_COMMIT_GOVERNOR: preserve rev009 count ceilings as\n        // hard rails, but stop consuming completed raster results by measured\n        // main-thread wall-clock budget after guaranteed minimum forward progress.\n        const double MainThreadCommitSteadyBudgetMilliseconds = 0.50;\n        const double MainThreadCommitBootstrapBudgetMilliseconds = 1.25;\n'''
renderer, c1 = replace_once(renderer, const_old, const_new, 'time budget constants')

fields_old = '''        long operationHealthContentHeadingCoalesced;\n'''
fields_new = '''        long operationHealthContentHeadingCoalesced;\n        readonly Stopwatch mainThreadCommitStopwatch = new Stopwatch();\n        long operationHealthMainCommitBudgetHits;\n        int operationHealthMainCommitBacklog;\n        int operationHealthMainCommitBacklogPeak;\n        double operationHealthMainCommitWindowMaxMilliseconds;\n        long operationHealthMainCommitOverbudget;\n        long operationHealthMainCommitProcessed;\n        double operationHealthMainCommitBudgetMilliseconds;\n'''
renderer, c2 = replace_once(renderer, fields_old, fields_new, 'governor telemetry fields')

if TAG not in renderer[renderer.find('void DrainCompleted(AERISTerrainTileSystem system)'):]:
    drain_old = '''            completed.Clear();\n            int profileMaximum = performance == null ? 2 :\n                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);\n            int burstMaximum = frontBufferValid && requestedViewReady ?\n                SteadyContentCommitMaximumResults : BootstrapContentCommitMaximumResults;\n            int maximum = Math.Max(1, Math.Min(profileMaximum, burstMaximum));\n            rasterizer.Drain(completed, maximum);\n            int deferredCompleted = Math.Max(0, rasterizer.CompletedCount);\n            if (deferredCompleted > 0)\n            {\n                operationHealthContentCommitBudgetHits++;\n                operationHealthContentCommitBacklogPeak = Math.Max(\n                    operationHealthContentCommitBacklogPeak, deferredCompleted);\n            }\n'''
    drain_new = '''            // AERIS25_MAIN_THREAD_COMMIT_GOVERNOR: consume one completed raster\n            // result at a time. The first result always runs; only subsequent work is\n            // deferred when the measured window reaches the profile budget.\n            int profileMaximum = performance == null ? 2 :\n                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);\n            bool steadyCommitProfile = frontBufferValid && requestedViewReady;\n            int burstMaximum = steadyCommitProfile ?\n                SteadyContentCommitMaximumResults : BootstrapContentCommitMaximumResults;\n            int hardMaximum = Math.Max(1, Math.Min(profileMaximum, burstMaximum));\n            double budgetMilliseconds = steadyCommitProfile ?\n                MainThreadCommitSteadyBudgetMilliseconds :\n                MainThreadCommitBootstrapBudgetMilliseconds;\n            operationHealthMainCommitBudgetMilliseconds = budgetMilliseconds;\n            int processedThisWindow = 0;\n            mainThreadCommitStopwatch.Reset();\n            mainThreadCommitStopwatch.Start();\n            while (processedThisWindow < hardMaximum)\n            {\n                completed.Clear();\n                if (rasterizer.Drain(completed, 1) <= 0) break;\n'''
    renderer, c3 = replace_once(renderer, drain_old, drain_new, 'DrainCompleted one-at-a-time prefix')

    method_name = '        void DrainCompleted(AERISTerrainTileSystem system)'
    method_start = renderer.find(method_name)
    if method_start < 0:
        raise SystemExit(PREFIX + ' DrainCompleted method not found')
    method_open = renderer.find('{', method_start)
    for_token = '            for (int i = 0; i < completed.Count; i++)'
    for_start = renderer.find(for_token, method_open)
    if for_start < 0:
        raise SystemExit(PREFIX + ' completed-result for loop not found')
    for_open = renderer.find('{', for_start)
    for_close = matching_brace(renderer, for_open)
    post = '''\n                processedThisWindow += completed.Count;\n                operationHealthMainCommitProcessed += completed.Count;\n                double elapsedMilliseconds = ElapsedMilliseconds(mainThreadCommitStopwatch);\n                operationHealthMainCommitWindowMaxMilliseconds = Math.Max(\n                    operationHealthMainCommitWindowMaxMilliseconds, elapsedMilliseconds);\n                int remainingCompleted = Math.Max(0, rasterizer.CompletedCount);\n                operationHealthMainCommitBacklog = remainingCompleted;\n                operationHealthMainCommitBacklogPeak = Math.Max(\n                    operationHealthMainCommitBacklogPeak, remainingCompleted);\n                if (gpuFailed) break;\n                if (elapsedMilliseconds >= budgetMilliseconds)\n                {\n                    if (remainingCompleted > 0) operationHealthMainCommitBudgetHits++;\n                    if (elapsedMilliseconds > budgetMilliseconds)\n                        operationHealthMainCommitOverbudget++;\n                    break;\n                }\n            }\n            mainThreadCommitStopwatch.Stop();\n            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount);\n            operationHealthMainCommitBacklog = finalRemainingCompleted;\n            operationHealthMainCommitBacklogPeak = Math.Max(\n                operationHealthMainCommitBacklogPeak, finalRemainingCompleted);\n            // Preserve ATROPINE rev009 count-cap telemetry as a hard-rail witness.\n            if (finalRemainingCompleted > 0 && processedThisWindow >= hardMaximum)\n                operationHealthContentCommitBudgetHits++;\n            operationHealthContentCommitBacklogPeak = Math.Max(\n                operationHealthContentCommitBacklogPeak, finalRemainingCompleted);\n'''
    renderer = renderer[:for_close + 1] + post + renderer[for_close + 1:]
    c4 = True
else:
    c3 = c4 = False

telemetry_old = '''                "; oh_presentation_packet_draw=" + operationHealthPresentationPacketDraws +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
telemetry_new = '''                "; oh_presentation_packet_draw=" + operationHealthPresentationPacketDraws +\n                "; oh_main_commit_budget_hit=" + operationHealthMainCommitBudgetHits +\n                "; oh_main_commit_backlog=" + operationHealthMainCommitBacklog +\n                "; oh_main_commit_backlog_peak=" + operationHealthMainCommitBacklogPeak +\n                "; oh_main_commit_window_max_ms=" + operationHealthMainCommitWindowMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +\n                "; oh_main_commit_overbudget=" + operationHealthMainCommitOverbudget +\n                "; oh_main_commit_processed=" + operationHealthMainCommitProcessed +\n                "; oh_main_commit_budget_ms=" + operationHealthMainCommitBudgetMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
renderer, c5 = replace_once(renderer, telemetry_old, telemetry_new, 'runtime telemetry')

if any((c1, c2, c3, c4, c5)):
    R.write_text(renderer)
    print(PREFIX + ' measured-time commit governor applied')
else:
    print(PREFIX + ' measured-time commit governor already present')

monitor = M.read_text()
monitor, m1 = replace_once(monitor,
    'internal const string Codename = "ADENOSINE";',
    'internal const string Codename = "NOREPINEPHRINE";', 'codename identity')
monitor, m2 = replace_once(monitor,
    'internal const string Revision = "OH_PHASE5_001";',
    'internal const string Revision = "OH_PHASE6_001";', 'revision identity')
monitor, m3 = replace_once(monitor,
    'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";',
    'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";', 'candidate identity')
if any((m1, m2, m3)):
    M.write_text(monitor)

config = C.read_text()
config, cfg1 = replace_once(config, 'codename = ADENOSINE',
                            'codename = NOREPINEPHRINE', 'config codename')
if cfg1:
    C.write_text(config)

build = U.read_text()
build, b1 = replace_once(build,
    'CANDIDATE_NAME="AERIS25_PERSISTENT_PRESENTATION_BATCHING"',
    'CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"', 'build candidate')
build, b2 = replace_once(build,
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 5 ADENOSINE PERSISTENT PRESENTATION BATCHING REV001"',
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001"',
    'build display')
build, b3 = replace_once(build,
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 5 ADENOSINE — PERSISTENT PRESENTATION BATCHING — REV001',
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001',
    'build checkpoint')
build, b4 = replace_once(build,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_persistent_presentation_batching.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_main_thread_commit_governor.py"',
    'active Phase6 verifier')
if any((b1, b2, b3, b4)):
    U.write_text(build)
    print(PREFIX + ' build identity/verifier promoted')
else:
    print(PREFIX + ' build identity/verifier already promoted')

# Inherited verifiers are explicit lineage contracts. Admit this exact Phase 6
# descendant without weakening them into open-ended future-phase checks.
p5v = P5V.read_text()
p5_identity_old = '''ck('internal const string Codename = "ADENOSINE";' in M and
   'internal const string Revision = "OH_PHASE5_001";' in M and
   'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";' in M and
   'codename = ADENOSINE' in C,
   'ADENOSINE OH_PHASE5_001 identity is authoritative')'''
p5_identity_new = '''phase5_identity = ('internal const string Codename = "ADENOSINE";' in M and
    'internal const string Revision = "OH_PHASE5_001";' in M and
    'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";' in M and
    'codename = ADENOSINE' in C)
phase6_identity = ('internal const string Codename = "NOREPINEPHRINE";' in M and
    'internal const string Revision = "OH_PHASE6_001";' in M and
    'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
    'codename = NOREPINEPHRINE' in C)
ck(phase5_identity or phase6_identity,
   'ADENOSINE Phase5 identity or approved NOREPINEPHRINE Phase6 descendant is authoritative')'''
p5v, pv1 = replace_once(p5v, p5_identity_old, p5_identity_new,
                         'Phase5 verifier identity descendant')
p5_build_old = '''ck('CANDIDATE_NAME="AERIS25_PERSISTENT_PRESENTATION_BATCHING"' in U and
   'OPERATION HEALTH PHASE 5 ADENOSINE PERSISTENT PRESENTATION BATCHING REV001' in U and
   'OPERATION HEALTH PHASE 5 ADENOSINE — PERSISTENT PRESENTATION BATCHING — REV001' in U,
   'build/in-game identity is AERIS25-2 ADENOSINE')'''
p5_build_new = '''phase5_build = ('CANDIDATE_NAME="AERIS25_PERSISTENT_PRESENTATION_BATCHING"' in U and
    'OPERATION HEALTH PHASE 5 ADENOSINE PERSISTENT PRESENTATION BATCHING REV001' in U and
    'OPERATION HEALTH PHASE 5 ADENOSINE — PERSISTENT PRESENTATION BATCHING — REV001' in U)
phase6_build = ('CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"' in U and
    'OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001' in U and
    'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001' in U)
ck(phase5_build or phase6_build,
   'build/in-game identity is ADENOSINE or approved NOREPINEPHRINE descendant')'''
p5v, pv2 = replace_once(p5v, p5_build_old, p5_build_new,
                         'Phase5 verifier build descendant')
p5_active_old = '''ck('verify_aeris25_persistent_presentation_batching.py' in active and
   'verify_aeris25_content_generation_burst_governor_hotfix.py' not in active and
   'verify_aeris25_chunk_cull_guard_hotfix.py' not in active and
   'verify_aeris25_temporal_foundation_overscan_hotfix.py' not in active,
   'Phase 5 build uses one final-tree verifier after inherited pre-promotion acceptance')'''
p5_active_new = '''ck((('verify_aeris25_persistent_presentation_batching.py' in active) or
    ('verify_aeris25_main_thread_commit_governor.py' in active)) and
   'verify_aeris25_content_generation_burst_governor_hotfix.py' not in active and
   'verify_aeris25_chunk_cull_guard_hotfix.py' not in active and
   'verify_aeris25_temporal_foundation_overscan_hotfix.py' not in active,
   'Phase 5 contract accepts its verifier or the approved Phase 6 final-tree verifier')'''
p5v, pv3 = replace_once(p5v, p5_active_old, p5_active_new,
                         'Phase5 verifier active build descendant')
if any((pv1, pv2, pv3)):
    P5V.write_text(p5v)
    print(PREFIX + ' inherited ADENOSINE verifier admits exact Phase6 descendant')

step2 = STEP2.read_text()
step2_old = '''phase5='ADE'+'NOSINE'
ck(('OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT' in B) or
   (('OPERATION HEALTH PHASE 3 '+phase3+' GPU VERTEX PROJECTION') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 4 '+phase4+' GPU DYNAMIC TERRAIN COLOUR') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 5 '+phase5+' PERSISTENT PRESENTATION BATCHING') in B),
   'Ubuntu build identifies Step 2 parent or approved Phase 3/4/5 successor')'''
step2_new = '''phase5='ADE'+'NOSINE'
phase6='NOREPI'+'NEPHRINE'
ck(('OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT' in B) or
   (('OPERATION HEALTH PHASE 3 '+phase3+' GPU VERTEX PROJECTION') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 4 '+phase4+' GPU DYNAMIC TERRAIN COLOUR') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 5 '+phase5+' PERSISTENT PRESENTATION BATCHING') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 6 '+phase6+' MAIN THREAD COMMIT GOVERNOR') in B),
   'Ubuntu build identifies Step 2 parent or approved Phase 3/4/5/6 successor')'''
step2, sv1 = replace_once(step2, step2_old, step2_new,
                           'Step2 Phase6 lineage')
if sv1:
    STEP2.write_text(step2)
    print(PREFIX + ' Step2 lineage admits exact Phase6 successor')

print(PREFIX + ' MAIN THREAD COMMIT GOVERNOR APPLIED')
print('Budgets: steady=0.50 ms, bootstrap=1.25 ms; rev009 hard ceilings remain 2/4 results')
print('Invariant: first completed result always progresses; visible 10 Hz / Golden / packet painter order unchanged')
