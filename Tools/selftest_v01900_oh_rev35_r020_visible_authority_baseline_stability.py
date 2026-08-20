#!/usr/bin/env python3
import sys

sys.dont_write_bytecode = True
PREFIX = '[OH REV3.5 R020 VISIBLE AUTHORITY BASELINE STABILITY]'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def delta_angle(a, b):
    return (b - a + 180.0) % 360.0 - 180.0


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
        heading_changed = (
            not authority_valid or
            (track_up and abs(delta_angle(self.base_heading, heading)) > 3.0))
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
for heading in (11.0, 12.0, 13.0):
    check(not h.sample(0.0, heading=heading),
          'track-up heading %.1fdeg retained at/below strict 3deg threshold' % heading)
check(abs(h.base_heading - 10.0) < 1e-9 and abs(h.live_heading - 13.0) < 1e-9,
      'sub-threshold heading samples do not drag baseline')
check(h.sample(0.0, heading=13.01),
      'cumulative track-up heading beyond 3deg advances generation')

n = AuthorityModel(track_up=False, anchor=0.5)
check(n.sample(0.0, heading=0.0), 'north-up first sample advances')
for heading in (45.0, 180.0, 359.0):
    check(not n.sample(0.0, heading=heading, track_up=False),
          'north-up heading change is not generation authority')
check(n.generation == 1, 'north-up heading-only motion leaves generation unchanged')

e = AuthorityModel()
check(e.sample(0.0, heading=20.0), 'strict-threshold model initialized')
check(not e.sample(3200.0, heading=23.0),
      'exact center/heading thresholds do not advance')
check(e.sample(3200.0001, heading=23.0),
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
print('policy=live sample follows every capture; generation baseline advances only after cumulative material threshold')
