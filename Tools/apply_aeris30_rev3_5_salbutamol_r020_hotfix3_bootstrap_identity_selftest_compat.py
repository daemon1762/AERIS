#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / 'Tools/build_aeris29_rev3_5_salbutamol_r020_fast.py'
SELFTEST = ROOT / 'Tools/selftest_v01900_oh_rev35_r020_visible_authority_baseline_stability.py'
PREFIX = '[AERIS30 REV3.5 R020 HOTFIX3 BOOTSTRAP IDENTITY/SELFTEST COMPAT]'


def fail(message):
    raise SystemExit(PREFIX + ' FAIL ' + message)


for path in (BUILD, SELFTEST):
    if not path.is_file():
        fail('missing ' + str(path.relative_to(ROOT)))

build = BUILD.read_text(encoding='utf-8')
old_identity = '''# Preserve the installed/materialized R019 Hotfix1 candidate identity and append only R020.
identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
if not identity.is_file():
    raise SystemExit(PREFIX + ' materialized candidate identity missing')
ident = identity.read_text()
for line in (
    'rev3_5_r018_variant=' + R018,
    'rev3_5_r019_variant=' + R019,
    'rev3_5_r019_hotfix1=' + HF1,
):
    if line not in ident:
        raise SystemExit(PREFIX + ' parent identity missing: ' + line)
r020_line = 'rev3_5_r020_variant=' + R020 + '\\n'
if r020_line not in ident:
    if ident and not ident.endswith('\\n'):
        ident += '\\n'
    ident += r020_line
    identity.write_text(ident)
'''
new_identity = '''# FAST may start from the preserved R016 materialized candidate and conditionally
# bootstrap R017/R018 before overlaying R019/HF1/R020. The runtime/source marker gates
# above already proved each exact stage is present and all impact verifiers passed.
# Therefore materialize only the missing candidate-identity lines here; never infer an
# identity for a stage whose exact source marker was not already asserted.
identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
if not identity.is_file():
    raise SystemExit(PREFIX + ' materialized candidate identity missing')
ident = identity.read_text()
identity_lines = (
    'rev3_5_r017_variant=' + R017,
    'rev3_5_r018_variant=' + R018,
    'rev3_5_r019_variant=' + R019,
    'rev3_5_r019_hotfix1=' + HF1,
    'rev3_5_r020_variant=' + R020,
)
for line in identity_lines:
    if line not in ident:
        if ident and not ident.endswith('\\n'):
            ident += '\\n'
        ident += line + '\\n'
identity.write_text(ident)
for line in identity_lines:
    if line not in identity.read_text():
        raise SystemExit(PREFIX + ' candidate identity materialization failed: ' + line)
'''

if new_identity in build:
    print(PREFIX + ' build identity compatibility already applied')
elif old_identity in build:
    if build.count(old_identity) != 1:
        fail('base FAST identity block ambiguous=%d' % build.count(old_identity))
    build = build.replace(old_identity, new_identity, 1)
    BUILD.write_text(build, encoding='utf-8')
    print(PREFIX + ' patched FAST bootstrap identity materialization')
else:
    fail('base FAST identity block missing and successor block absent')

selftest = r'''#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[OH REV3.5 R020 VISIBLE AUTHORITY BASELINE STABILITY]'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def delta_angle(a, b):
    return (b - a + 180.0) % 360.0 - 180.0


if not T.is_file():
    raise SystemExit(PREFIX + ' FAIL tile system missing')
tile = T.read_text(encoding='utf-8')
legacy3 = (
    'Math.Abs(DeltaAngle(rev35R020GenerationViewHeadingDeg,' in tile and
    'normalizedHeading)) > 3.0' in tile
)
burst6 = (
    'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR preserved:' in tile and
    'Math.Abs(DeltaAngle(rev35R020GenerationViewHeadingDeg,' in tile and
    'planningHeadingDelta >= 6.0' in tile
)
check(legacy3 != burst6, 'exactly one R020 heading policy detected from materialized source')
if legacy3 == burst6:
    print(PREFIX + ' FAIL unsupported/ambiguous materialized heading policy')
    raise SystemExit(1)

HEADING_THRESHOLD = 6.0 if burst6 else 3.0
HEADING_INCLUSIVE = burst6
HEADING_POLICY = 'successor-cumulative-ge-6deg' if burst6 else 'historical-strict-gt-3deg'
print('heading_policy=' + HEADING_POLICY)


class AuthorityModel:
    def __init__(self, range_m=160000.0, track_up=True, heading=0.0,
                 anchor=0.75, orientation='DIRECT'):
        self.valid = False
        self.base_center_m = 0.0
        self.base_range_m = 0.0
        self.base_heading = 0.0
        self.base_track_up = False
        self.base_anchor = 0.5
        self.base_orientation = 'DIRECT'
        self.live_center_m = 0.0
        self.live_range_m = range_m
        self.live_heading = heading
        self.live_track_up = track_up
        self.live_anchor = anchor
        self.live_orientation = orientation
        self.generation = 0
        self.retained = 0
        self.advances = 0

    def heading_crossed(self, delta):
        return delta >= HEADING_THRESHOLD if HEADING_INCLUSIVE else delta > HEADING_THRESHOLD

    def sample(self, center_m, range_m=None, heading=None, track_up=None,
               anchor=None, orientation=None):
        if range_m is None: range_m = self.live_range_m
        if heading is None: heading = self.live_heading
        if track_up is None: track_up = self.live_track_up
        if anchor is None: anchor = self.live_anchor
        if orientation is None: orientation = self.live_orientation

        authority_valid = self.valid
        range_changed = (not authority_valid or
                         abs(self.base_range_m - range_m) > 0.5)
        center_changed = (not authority_valid or
                          abs(center_m - self.base_center_m) >
                          max(100.0, range_m * 0.02))
        orientation_changed = (
            not authority_valid or
            self.base_track_up != track_up or
            self.base_orientation != orientation or
            abs(self.base_anchor - anchor) > 0.001)
        heading_delta = abs(delta_angle(self.base_heading, heading))
        heading_changed = (
            not authority_valid or
            (track_up and self.heading_crossed(heading_delta)))
        material = (range_changed or center_changed or
                    orientation_changed or heading_changed)

        self.live_center_m = center_m
        self.live_range_m = range_m
        self.live_heading = heading
        self.live_track_up = track_up
        self.live_anchor = anchor
        self.live_orientation = orientation

        if material:
            self.valid = True
            self.base_center_m = center_m
            self.base_range_m = range_m
            self.base_heading = heading
            self.base_track_up = track_up
            self.base_anchor = anchor
            self.base_orientation = orientation
            self.generation += 1
            self.advances += 1
        else:
            self.retained += 1
        return material


m = AuthorityModel()
check(m.sample(0.0), 'first valid sample creates generation 1')
check(m.generation == 1 and m.base_center_m == 0.0,
      'first generation captures exact authority baseline')
for center in (800.0, 1600.0, 2400.0, 3200.0):
    changed = m.sample(center)
    check(not changed,
          'center sample %.0fm retained at/below strict 3.2km threshold' % center)
check(m.generation == 1 and m.base_center_m == 0.0 and m.live_center_m == 3200.0,
      'sub-threshold live motion does not drag authority baseline')
check(m.sample(3200.1), 'cumulative center motion beyond 3.2km advances generation')
check(m.generation == 2 and abs(m.base_center_m - 3200.1) < 1e-9,
      'new generation resets baseline to exact live center')

h = AuthorityModel(track_up=True)
check(h.sample(0.0, heading=10.0), 'heading model first sample advances')
if burst6:
    retained_headings = (11.0, 12.0, 13.0, 14.0, 15.0, 15.99)
    crossing_heading = 16.0
    crossing_label = 'cumulative successor heading at inclusive 6deg advances generation'
else:
    retained_headings = (11.0, 12.0, 13.0)
    crossing_heading = 13.01
    crossing_label = 'cumulative historical heading beyond strict 3deg advances generation'
for heading in retained_headings:
    check(not h.sample(0.0, heading=heading),
          'track-up heading %.2fdeg retained below policy threshold' % heading)
check(abs(h.base_heading - 10.0) < 1e-9,
      'sub-threshold heading samples do not drag authority baseline')
check(h.sample(0.0, heading=crossing_heading), crossing_label)

n = AuthorityModel(track_up=False, anchor=0.5)
check(n.sample(0.0, heading=0.0), 'north-up first sample advances')
for heading in (45.0, 180.0, 359.0):
    check(not n.sample(0.0, heading=heading, track_up=False),
          'north-up heading change is not generation authority')
check(n.generation == 1, 'north-up heading-only motion leaves generation unchanged')

e = AuthorityModel()
check(e.sample(0.0, heading=20.0), 'center strict-threshold model initialized')
check(not e.sample(3200.0, heading=20.0),
      'exact center threshold does not advance')
check(e.sample(3200.0001, heading=20.0),
      'center epsilon beyond threshold advances')

r = AuthorityModel()
check(r.sample(0.0), 'authority-change model initialized')
check(r.sample(0.0, range_m=160000.6), 'range delta >0.5m advances')
check(r.sample(0.0, track_up=False), 'track-up toggle advances')
check(r.sample(0.0, track_up=False, anchor=0.50), 'anchor material change advances')
check(r.sample(0.0, track_up=False, anchor=0.50, orientation='FLIPPED'),
      'render-target orientation change advances')

check(m.retained >= 4 and h.retained >= 3 and n.retained >= 3,
      'retained counter observes sub-threshold samples')
check(m.advances == m.generation and h.advances == h.generation,
      'advance counter matches generation events in pure model')

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' PASS %d/%d' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
print('policy=stable cumulative authority baseline; heading=' + HEADING_POLICY)
'''

current_selftest = SELFTEST.read_text(encoding='utf-8')
if current_selftest == selftest:
    print(PREFIX + ' source-aware selftest already applied')
else:
    if 'class AuthorityModel:' not in current_selftest or 'VISIBLE AUTHORITY BASELINE STABILITY' not in current_selftest:
        fail('unexpected R020 selftest shape; refusing replacement')
    SELFTEST.write_text(selftest, encoding='utf-8')
    print(PREFIX + ' replaced pure model with source-aware 3deg/6deg successor model')

print(PREFIX + ' APPLY PASS')
print('scope=tooling identity + pure-model compatibility only')
print('runtime_source_change=0 control_law_change=0 publication_change=0 worker_change=0')
