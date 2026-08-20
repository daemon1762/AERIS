#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R020 VISIBLE AUTHORITY BASELINE STABILITY]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'


def fail(message):
    raise SystemExit(PREFIX + ' FAIL ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor count=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0:
        fail('method open missing: ' + signature)
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
                if depth == 0: return start, i + 1
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
    fail('method close missing: ' + signature)


def patch_reset_method(text, signature):
    start, end = method_bounds(text, signature)
    body = text[start:end]
    old = '            displayViewValid = false;\n'
    new = old + '            rev35R020GenerationViewValid = false;\n'
    body, changed = replace_once(body, old, new,
                                 'R020 authority baseline reset in ' + signature.strip())
    return text[:start] + body + text[end:], changed


for path in (T, R, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

tile = T.read_text()
renderer = R.read_text()
build = B.read_text()
prebuild = PRE.read_text()

for required in (R018, R019, HF1):
    if required not in renderer:
        fail('materialized parent missing from renderer: ' + required)

# R020 fixes only the definition of a material viewport generation. The live displayView*
# sample remains current on every CaptureVisible() call, while a separate baseline remains
# fixed until one of the already-existing material thresholds is actually crossed.
if R020 not in tile:
    fields_old = '''        AERISTerrainRenderTargetOrientation displayViewOrientation =
            AERISTerrainRenderTargetOrientation.Direct;
        int lastFoundationRequestedCount;
'''
    fields_new = '''        AERISTerrainRenderTargetOrientation displayViewOrientation =
            AERISTerrainRenderTargetOrientation.Direct;
        // ''' + R020 + ''': displayView* remains the latest live planner sample.
        // These fields are the stable authority baseline used only for deciding when the
        // existing View/Range/Plan generations advance. Sub-threshold samples must not
        // drag the comparison origin forward and erase cumulative aircraft motion/heading.
        internal const string Rev35R020Variant = "''' + R020 + '''";
        bool rev35R020GenerationViewValid;
        double rev35R020GenerationViewLatitudeDeg;
        double rev35R020GenerationViewLongitudeDeg;
        double rev35R020GenerationViewRangeMeters;
        double rev35R020GenerationViewHeadingDeg;
        bool rev35R020GenerationViewTrackUp;
        float rev35R020GenerationViewAnchorGuiV = 0.5f;
        AERISTerrainRenderTargetOrientation rev35R020GenerationViewOrientation =
            AERISTerrainRenderTargetOrientation.Direct;
        long operationHealthRev35R020AuthoritySamples;
        long operationHealthRev35R020GenerationRetained;
        long operationHealthRev35R020GenerationAdvances;
        int lastFoundationRequestedCount;
'''
    tile, _ = replace_once(tile, fields_old, fields_new,
                           'R020 stable authority baseline fields')

    prop_old = '''        internal long ViewGeneration { get { return viewGeneration; } }
        internal string ActiveBodyName { get { return activeBodyName; } }
'''
    prop_new = '''        internal long ViewGeneration { get { return viewGeneration; } }
        internal long Rev35R020AuthoritySampleCount
        {
            get { return operationHealthRev35R020AuthoritySamples; }
        }
        internal long Rev35R020GenerationRetainedCount
        {
            get { return operationHealthRev35R020GenerationRetained; }
        }
        internal long Rev35R020GenerationAdvanceCount
        {
            get { return operationHealthRev35R020GenerationAdvances; }
        }
        internal string ActiveBodyName { get { return activeBodyName; } }
'''
    tile, _ = replace_once(tile, prop_old, prop_new,
                           'R020 telemetry accessors')

    for signature in (
        '        internal void Reset(string reason)',
        '        void SuspendFlightViewport()',
        '        void ResumeFlightViewport()',
        '        void BeginBody(CelestialBody body)',
    ):
        tile, _ = patch_reset_method(tile, signature)

    m0, m1 = method_bounds(tile,
        '        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,')
    old_method = tile[m0:m1]
    for required in (
        'Math.Abs(displayViewRangeMeters - normalizedRange) > 0.5',
        'GreatCircleDistanceMeters(activeBody, displayViewLatitudeDeg,',
        'Math.Max(100.0, normalizedRange * 0.02)',
        'Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0',
        'displayViewLatitudeDeg = normalizedLatitude;',
        'if (rangeChanged) rangeGeneration++;',
        'if (centerChanged || orientationChanged || headingChanged) planGeneration++;',
        'viewGeneration++;',
    ):
        if required not in old_method:
            fail('legacy UpdateDisplayView contract missing: ' + required)

    new_method = r'''        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,
            double rangeMeters, double headingDeg, bool trackUp, float anchorGuiV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (!IsFinite(latitudeDeg) || !IsFinite(longitudeDeg) ||
                !IsFinite(rangeMeters) || rangeMeters <= 0.0) return;
            double normalizedLatitude = Math.Max(-90.0, Math.Min(90.0, latitudeDeg));
            double normalizedLongitude = NormalizeLongitude(longitudeDeg);
            // Internal presentation planning may deliberately use a non-UI overscan
            // range (Gate 4B temporal history surface). Do not snap it back to the
            // 5/10/20/40/80/160 km user selector steps. The public UI range remains
            // normalized before it reaches the renderer; this value is planner-only.
            double normalizedRange = Math.Max(1000.0, Math.Min(250000.0, rangeMeters));
            double normalizedHeading = NormalizeHeading(headingDeg);
            float normalizedAnchor = Mathf.Clamp01(anchorGuiV);

            operationHealthRev35R020AuthoritySamples++;
            bool authorityValid = rev35R020GenerationViewValid;
            bool rangeChanged = !authorityValid ||
                Math.Abs(rev35R020GenerationViewRangeMeters - normalizedRange) > 0.5;
            double centerMovement = !authorityValid ? double.MaxValue :
                GreatCircleDistanceMeters(activeBody,
                    rev35R020GenerationViewLatitudeDeg,
                    rev35R020GenerationViewLongitudeDeg,
                    normalizedLatitude, normalizedLongitude);
            bool centerChanged = !authorityValid || centerMovement >
                Math.Max(100.0, normalizedRange * 0.02);
            bool orientationChanged = !authorityValid ||
                rev35R020GenerationViewTrackUp != trackUp ||
                rev35R020GenerationViewOrientation != orientation ||
                Math.Abs(rev35R020GenerationViewAnchorGuiV - normalizedAnchor) > 0.001f;
            bool headingChanged = !authorityValid || (trackUp &&
                Math.Abs(DeltaAngle(rev35R020GenerationViewHeadingDeg,
                    normalizedHeading)) > 3.0);
            bool materiallyChanged = rangeChanged || centerChanged ||
                orientationChanged || headingChanged;

            // Live planning always follows the newest sample, exactly as before R020.
            displayViewValid = true;
            displayViewLatitudeDeg = normalizedLatitude;
            displayViewLongitudeDeg = normalizedLongitude;
            displayViewRangeMeters = normalizedRange;
            displayViewHeadingDeg = normalizedHeading;
            displayViewTrackUp = trackUp;
            displayViewAnchorGuiV = normalizedAnchor;
            displayViewOrientation = orientation;

            if (materiallyChanged)
            {
                // Advance the stable authority baseline atomically with the generations.
                // A new baseline represents the exact live view for the new generation.
                rev35R020GenerationViewValid = true;
                rev35R020GenerationViewLatitudeDeg = normalizedLatitude;
                rev35R020GenerationViewLongitudeDeg = normalizedLongitude;
                rev35R020GenerationViewRangeMeters = normalizedRange;
                rev35R020GenerationViewHeadingDeg = normalizedHeading;
                rev35R020GenerationViewTrackUp = trackUp;
                rev35R020GenerationViewAnchorGuiV = normalizedAnchor;
                rev35R020GenerationViewOrientation = orientation;
                if (rangeChanged) rangeGeneration++;
                if (centerChanged || orientationChanged || headingChanged)
                    planGeneration++;
                viewGeneration++;
                operationHealthRev35R020GenerationAdvances++;
                firstViewRequestRealtime = Time.realtimeSinceStartup;
                preloadTelemetry.FirstTileVisibleMilliseconds = 0.0;
                if (nextPlanRealtime - Time.realtimeSinceStartup > 0.05f)
                    nextPlanRealtime = Time.realtimeSinceStartup + 0.05f;
            }
            else
            {
                operationHealthRev35R020GenerationRetained++;
            }
        }'''
    tile = tile[:m0] + new_method + tile[m1:]
else:
    print(PREFIX + ' tile-system overlay already present')

# Telemetry is read-only: mirror the tile-system counters into the existing OH line.
if R020 not in renderer:
    identity_old = '        const string Rev35R019Hotfix1Variant = "' + HF1 + '";\n'
    identity_new = identity_old + (
        '        // ' + R020 + ': read-only witness for TileSystem authority-generation baseline.\n'
        '        const string Rev35R020Variant = "' + R020 + '";\n'
        '        long operationHealthRev35R020AuthoritySamples;\n'
        '        long operationHealthRev35R020GenerationRetained;\n'
        '        long operationHealthRev35R020GenerationAdvances;\n')
    renderer, _ = replace_once(renderer, identity_old, identity_new,
                               'R020 renderer telemetry identity')

    log_old = '''            LogGpuOnlyPresentation(visible, readyGlobal, readyFar, swapped);
'''
    log_new = '''            operationHealthRev35R020AuthoritySamples =
                system.Rev35R020AuthoritySampleCount;
            operationHealthRev35R020GenerationRetained =
                system.Rev35R020GenerationRetainedCount;
            operationHealthRev35R020GenerationAdvances =
                system.Rev35R020GenerationAdvanceCount;
            LogGpuOnlyPresentation(visible, readyGlobal, readyFar, swapped);
'''
    renderer, _ = replace_once(renderer, log_old, log_new,
                               'R020 telemetry counter mirror')

    telemetry_old = (
        '                "; oh_rev35_r019_hf1_variant=" + Rev35R019Hotfix1Variant +\n')
    telemetry_new = telemetry_old + (
        '                "; oh_rev35_r020_variant=" + Rev35R020Variant +\n'
        '                "; oh_rev35_r020_authority_samples=" + '
        'operationHealthRev35R020AuthoritySamples +\n'
        '                "; oh_rev35_r020_generation_retained=" + '
        'operationHealthRev35R020GenerationRetained +\n'
        '                "; oh_rev35_r020_generation_advance=" + '
        'operationHealthRev35R020GenerationAdvances +\n')
    renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                               'R020 runtime telemetry')
else:
    print(PREFIX + ' renderer telemetry overlay already present')

# FORMAL build wiring. FAST does not invoke build_ubuntu.sh, but keeping the formal
# materialization identity/verifier chain correct avoids a second ad-hoc patch later.
r019_var = 'REV3_5_R019_VARIANT="' + R019 + '"\n'
r020_var = r019_var + 'REV3_5_R020_VARIANT="' + R020 + '"\n'
build, _ = replace_once(build, r019_var, r020_var,
                        'R020 build identity variable')

r019_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py"\n')
r020_verify = r019_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py"\n')
build, _ = replace_once(build, r019_verify, r020_verify,
                        'R020 build verifier')

r019_identity = (
    'printf \'rev3_5_r019_variant=%s\\n\' "$REV3_5_R019_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r020_identity = r019_identity + (
    'printf \'rev3_5_r020_variant=%s\\n\' "$REV3_5_R020_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r019_identity, r020_identity,
                        'R020 candidate identity')

pre_anchor = (
    " ('OH REV3.5 R019 Visible FAR Commit Priority',"
    "'selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py'),\n")
pre_insert = pre_anchor + (
    " ('OH REV3.5 R020 Visible Authority Baseline Stability',"
    "'selftest_v01900_oh_rev35_r020_visible_authority_baseline_stability.py'),\n")
prebuild, _ = replace_once(prebuild, pre_anchor, pre_insert,
                           'R020 prebuild selftest')

required_tile = (
    R020,
    'rev35R020GenerationViewValid',
    'rev35R020GenerationViewLatitudeDeg',
    'operationHealthRev35R020AuthoritySamples++',
    'operationHealthRev35R020GenerationAdvances++',
    'operationHealthRev35R020GenerationRetained++',
)
for token in required_tile:
    if token not in tile:
        fail('tile-system contract incomplete: ' + token)
for token in (
    'oh_rev35_r020_variant=',
    'oh_rev35_r020_authority_samples=',
    'oh_rev35_r020_generation_retained=',
    'oh_rev35_r020_generation_advance=',
):
    if token not in renderer:
        fail('renderer telemetry incomplete: ' + token)

T.write_text(tile)
R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)

print(PREFIX + ' APPLY PASS')
print('authority=latest live display sample + separate last-generation baseline')
print('thresholds=range>0.5m center>max(100m,2% range) trackup/anchor/orientation heading>3deg')
print('generation=view/range/plan advance from one stable baseline; sub-threshold samples retain baseline')
print('publication=R017/R018/R019/HF1 untouched; stale BACK relabel/promotion=NONE')
print('worker_change=0 rasterizer_change=0 quality_change=0 10Hz_change=0 exact_range_change=0')
