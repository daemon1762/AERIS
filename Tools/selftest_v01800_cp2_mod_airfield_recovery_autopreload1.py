#!/usr/bin/env python3
import io
import struct
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite('v0.18.0.0 CP2 mod airfield recovery + auto preload progression 1')
contracts = read(ROOT / 'Source/AERISFlightControl/Landing/AERISRunwaySurveyContracts.cs')
snapshot = read(ROOT / 'Source/AERISFlightControl/Landing/AERISRunwaySnapshotBuilder.cs')
worker = read(ROOT / 'Source/AERISFlightControl/Landing/AERISRunwayGeometryWorker.cs')
preload = read(ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs')
generated = read(ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build = read(ROOT / 'build_ubuntu.sh')
version = read(ROOT / 'GameData/AERISFlightControl/AERISFlightControl.version')
readme = read(ROOT / 'README.md')

suite.check('CurrentModAirfieldRecoveryRevision = 1' in contracts,
            'targeted MOD-airfield cache recovery revision exists')
suite.check('KK_MOD_AIRFIELD_RECOVERY' in snapshot and
            'CurrentModAirfieldRecoveryRevision' in snapshot,
            'KK/SLE failed cache entries are forced through the recovered survey path')
suite.check('independentSurfaceAxis &&' in worker and
            '!values[i].IndependentSurfaceAxis' in worker and
            'bool replaceAxis' in worker,
            'independent pavement axis cannot be overwritten by a metadata primitive')
suite.check('RunwaySurfaceFlatnessEvidence' in worker,
            'slope gate has a dedicated landing-surface evidence filter')
flat = worker[worker.find('static bool RunwaySurfaceFlatnessEvidence'):
              worker.find('static bool ApplyAbsolutePlacementConstraint')]
for token in ('Runway', 'Pavement', 'Centerline', 'Taxiway', 'Apron',
              'Platform', 'Obstacle', 'NaturalSurface', 'ApproachLight', 'EdgeLight'):
    suite.check(token in flat, 'flatness filter handles semantic: ' + token)
suite.check('flatnessWeight > 1e-9' in worker,
            'decorative geometry cannot manufacture a false slope failure')
suite.check('surfaceError <= 1.0' in worker,
            'direct one-degree physical-axis agreement remains accepted')
suite.check('surfaceError <= 12.0' in worker and
            'ReRegisterCandidateToPhysicalAxis' in worker,
            'bounded mismatches are re-registered to the measured runway surface')
suite.check('candidate.PhysicalStartMeters - oldMidpoint' in worker and
            'candidate.OperationalThresholdB - oldMidpoint' in worker,
            'axis correction preserves physical, usable and operational along distances')
suite.check('correctionLimit=12.00deg' in worker,
            'large or ambiguous angle corrections remain fail-closed')
suite.check('axisReferenceError <= 15.0' in worker,
            'launch anchor remains an independent broad axis sanity gate')

for token in ('AutomaticComplete', 'CompletedQualityLimit',
              'CompletedEnvironmentHash', 'QualityOverride',
              'AutomaticPointRefinementOnly'):
    suite.check(token in preload, 'preload plan state exists: ' + token)
suite.check('!AutomaticTargetComplete(candidate)' in preload,
            'completed current-body plan is skipped instead of monopolising selection')
suite.check('MarkAutomaticComplete(plan)' in preload,
            'a full cyclic DB scan records automatic completion')
suite.check('TryAdvanceAutomaticPlan(currentBody)' in preload,
            'automatic progression runs when broad targets are complete')
suite.check('candidate.QualityLimit != AERISTerrainTileLod.Far' in preload and
            'selected.QualityLimit = AERISTerrainTileLod.Land' in preload,
            'high-priority registered sites advance from Far coverage to Land detail')
suite.check('!plan.AutomaticPointRefinementOnly' in preload and
            'AERISTerrainTileLod.Route' in preload,
            'automatic site refinement does not silently trigger a global Route build')
suite.check('HasPreloadPointsLocked' in preload,
            'only bodies with useful registered/current sites receive automatic Land refinement')
suite.check('version != 1 && version != 2' in preload and 'writer.Write(2)' in preload,
            'preload state V2 is written while V1 remains readable')
suite.check('plan.QualityOverride = true' in preload,
            'manual quality choice overrides automatic promotion')
suite.check('EnsureEnvironment(plan, body)' in preload,
            'all solid-body completion markers are invalidated by environment changes')
suite.check('[PRELOAD_AUTO]' in preload and 'event=PROMOTE' in preload and
            'event=COMPLETE' in preload,
            'field logs expose automatic body progression and completion')

# Scheduler mirror: current Kerbin may lead while incomplete, but once marked complete
# it must yield to another solid body. After all Far targets complete, only a high body
# with registered sites is promoted to point-only Land detail.
plans = [
    dict(name='Kerbin', priority=3, current=True, complete=True, quality=1,
         points=True, override=False),
    dict(name='Mun', priority=2, current=False, complete=False, quality=1,
         points=False, override=False),
    dict(name='Duna', priority=1, current=False, complete=False, quality=1,
         points=False, override=False),
]
eligible = [p for p in plans if not p['complete']]
eligible.sort(key=lambda p: (not p['current'], -p['priority'], p['name']))
suite.check(eligible[0]['name'] == 'Mun',
            'completed Kerbin yields automatic generation to another planet')
for p in plans:
    p['complete'] = True
promotion = [p for p in plans if p['complete'] and p['quality'] == 1 and
             p['priority'] >= 3 and p['points'] and not p['override']]
suite.check([p['name'] for p in promotion] == ['Kerbin'],
            'breadth-first planet coverage is followed by bounded Kerbin site refinement')

# Minimal binary compatibility mirror of the state-version prefix.
def wstr(stream, value):
    data = value.encode('utf-8')
    stream.write(bytes([len(data)]))
    stream.write(data)

def rstr(stream):
    n = stream.read(1)[0]
    return stream.read(n).decode('utf-8')
for version_number in (1, 2):
    stream = io.BytesIO()
    wstr(stream, 'AERIS_PRELOAD_TERRAIN_STATE_V1')
    stream.write(struct.pack('<i', version_number))
    stream.seek(0)
    suite.check(rstr(stream) == 'AERIS_PRELOAD_TERRAIN_STATE_V1' and
                struct.unpack('<i', stream.read(4))[0] in (1, 2),
                'state prefix remains readable for version ' + str(version_number))

identity = ('MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1')
suite.check(identity in generated and identity in build,
            'native build identity exposes both fixes')
suite.check('Mod Airfield Recovery Hotfix 1 Auto Preload Progression 1' in version,
            'AVC metadata exposes both fixes')
suite.check('Mod Airfield Recovery Hotfix 1 + Auto Preload Progression 1' in readme,
            'README identifies the current package')
suite.check('FlightCtrlState' not in strip_csharp_comments_and_literals(worker + preload),
            'recovery and preload progression remain flight-control free')
suite.check('MainThrottle' not in strip_csharp_comments_and_literals(worker + preload),
            'recovery and preload progression cannot command throttle')

for rel in (
    'Docs/CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_PROGRESSION_1_v0.18.0.0_ja.md',
    'Docs/ND_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_PROGRESSION_1_TEST_CARD_v0.18.0.0_ja.md',
    'Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD1_ja.md',
    'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD1.txt'):
    suite.check((ROOT / rel).is_file(), 'current recovery document exists: ' + rel)

suite.finish()
