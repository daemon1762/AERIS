#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py'
PREFIX = '[AERIS29 REV3.5 R020 VISIBLE AUTHORITY BASELINE STABILITY VERIFY]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return text[start:i + 1]
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
    return ''


for path in (T, R, B, PRE, A):
    check(path.is_file(), 'file exists: ' + str(path.relative_to(ROOT)))
if not all(path.is_file() for path in (T, R, B, PRE, A)):
    raise SystemExit(1)

tile = T.read_text()
renderer = R.read_text()
build = B.read_text()
prebuild = PRE.read_text()
applicator = A.read_text()

for token, label in (
    (R018, 'R018 presentation parent retained'),
    (R019, 'R019 visible commit-priority parent retained'),
    (HF1, 'R019 Hotfix1 wake/backlog parent retained'),
):
    check(token in renderer, label)
check(R020 in tile and R020 in renderer,
      'R020 identity present in tile-system and telemetry renderer')

method = method_body(tile,
    '        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,')
check(bool(method), 'UpdateDisplayView resolved')
check('operationHealthRev35R020AuthoritySamples++;' in method,
      'each valid display sample is observable')
check('bool authorityValid = rev35R020GenerationViewValid;' in method,
      'material comparison uses dedicated stable validity')
check('Math.Abs(rev35R020GenerationViewRangeMeters - normalizedRange) > 0.5' in method,
      'range threshold remains strict >0.5 m')
check('GreatCircleDistanceMeters(activeBody,' in method and
      'rev35R020GenerationViewLatitudeDeg' in method and
      'rev35R020GenerationViewLongitudeDeg' in method,
      'center displacement compares against last-generation baseline')
check('centerMovement >' in method and
      'Math.Max(100.0, normalizedRange * 0.02)' in method,
      'center threshold remains strict > max(100m, 2% range)')
check('rev35R020GenerationViewTrackUp != trackUp' in method and
      'rev35R020GenerationViewOrientation != orientation' in method and
      'Math.Abs(rev35R020GenerationViewAnchorGuiV - normalizedAnchor) > 0.001f' in method,
      'track-up/orientation/anchor authority preserved')
check('Math.Abs(DeltaAngle(rev35R020GenerationViewHeadingDeg,' in method and
      'normalizedHeading)) > 3.0' in method,
      'track-up heading threshold remains strict >3 degrees')

for token in (
    'displayViewLatitudeDeg = normalizedLatitude;',
    'displayViewLongitudeDeg = normalizedLongitude;',
    'displayViewRangeMeters = normalizedRange;',
    'displayViewHeadingDeg = normalizedHeading;',
    'displayViewTrackUp = trackUp;',
    'displayViewAnchorGuiV = normalizedAnchor;',
    'displayViewOrientation = orientation;',
):
    check(token in method, 'live planner sample retained: ' + token)

material = method.find('            if (materiallyChanged)')
retained = method.find('            else', material)
check(material >= 0 and retained > material, 'material/retained branches resolved')
before_material = method[:material] if material >= 0 else method
material_block = method[material:retained] if retained > material else ''
check('rev35R020GenerationViewLatitudeDeg = normalizedLatitude;' not in before_material,
      'authority baseline is not dragged forward by sub-threshold samples')
for token in (
    'rev35R020GenerationViewValid = true;',
    'rev35R020GenerationViewLatitudeDeg = normalizedLatitude;',
    'rev35R020GenerationViewLongitudeDeg = normalizedLongitude;',
    'rev35R020GenerationViewRangeMeters = normalizedRange;',
    'rev35R020GenerationViewHeadingDeg = normalizedHeading;',
    'rev35R020GenerationViewTrackUp = trackUp;',
    'rev35R020GenerationViewAnchorGuiV = normalizedAnchor;',
    'rev35R020GenerationViewOrientation = orientation;',
):
    check(token in material_block,
          'new generation atomically captures baseline: ' + token)
check('if (rangeChanged) rangeGeneration++;' in material_block,
      'range generation uses same material baseline')
check('if (centerChanged || orientationChanged || headingChanged)' in material_block and
      'planGeneration++;' in material_block,
      'plan generation uses same material baseline')
check('viewGeneration++;' in material_block and
      'operationHealthRev35R020GenerationAdvances++;' in material_block,
      'view generation advances exactly on material branch')
check('operationHealthRev35R020GenerationRetained++;' in method[retained:],
      'sub-threshold retention is observable')

for signature in (
    '        internal void Reset(string reason)',
    '        void SuspendFlightViewport()',
    '        void ResumeFlightViewport()',
    '        void BeginBody(CelestialBody body)',
):
    body = method_body(tile, signature)
    check('rev35R020GenerationViewValid = false;' in body,
          'authority baseline invalidated by ' + signature.strip())

check('foundationComplete = rendered && r018VisibleGpuComplete;' in renderer,
      'R018 exact-visible FRONT gate remains authoritative')
check('TryBeginRev35R019VisibleFoundationCommit()' in renderer,
      'R019 visible commit priority remains present')
check('rev35R019VisibleFoundationQueue.Count > 0' in renderer,
      'R019 Hotfix1 visible queue wake/backlog remains present')
for token in (
    'oh_rev35_r020_variant=',
    'oh_rev35_r020_authority_samples=',
    'oh_rev35_r020_generation_retained=',
    'oh_rev35_r020_generation_advance=',
):
    check(token in renderer, 'runtime telemetry ' + token)

check('REV3_5_R020_VARIANT="' + R020 + '"' in build,
      'formal build records R020 identity')
check('verify_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py' in build,
      'formal build invokes R020 verifier')
check('rev3_5_r020_variant=%s' in build,
      'candidate identity records R020')
check('selftest_v01900_oh_rev35_r020_visible_authority_baseline_stability.py' in prebuild,
      'R020 selftest wired into prebuild')

for forbidden in (
    'backRevision = currentViewRevision',
    'backViewGeneration = currentViewGeneration',
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    check(forbidden not in applicator,
          'R020 applicator excludes rejected mechanism: ' + forbidden)

for forbidden_path in (
    'AERISWorkerScheduler.cs', 'AERISTerrainGpuTileRasterizer.cs',
    'Source/AERISFlightControl/AA', 'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing',
):
    check(forbidden_path not in applicator,
          'R020 applicator does not target ' + forbidden_path)

settings = (ROOT / 'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
check('internal const float FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed visible 10 Hz authority retained')
check('RenderTextureFormat.ARGB32' in renderer and 'FilterMode.Bilinear' in renderer,
      'Golden ARGB32/Bilinear retained')
check('HistoryOverscanScale = 1.35f' in renderer and
      'MaximumHistorySurfaceRangeMeters = 250000f' in renderer,
      'hidden overscan preparation authority unchanged')

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
print('contract=stable last-generation authority baseline; live display sample still updates every capture')
print('publication=R017/R018/R019/HF1 unchanged; stale BACK promotion/relabel forbidden')
