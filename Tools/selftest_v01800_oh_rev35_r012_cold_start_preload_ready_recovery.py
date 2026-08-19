#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
N = ROOT / 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
S = ROOT / 'Source/AERISFlightControl/Settings/AERISSettings.cs'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
R012 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY'
PREFIX = '[OH REV3.5 R012 COLD START PRELOAD READY RECOVERY]'

for path in (R, O, P, N, B, PRE, S):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text(); observer = O.read_text(); preload = P.read_text()
nav = N.read_text(); build = B.read_text(); prebuild = PRE.read_text()
settings = S.read_text()

checks = []
def check(value, label):
    checks.append((bool(value), label))

check(R010 in renderer, 'R010 renderer lineage retained')
check('[OH_REV3_5_R011_TURN_CHURN]' in observer, 'R011 measurement observer retained')
check(R012 not in renderer, 'R012 does not modify renderer hot path')
check('string appliedPointSetSignature = string.Empty;' in preload,
      'applied preload point-set signature is tracked separately')
check('bool deferredPointSetInvalidation;' in preload,
      'deferred Flight point-set invalidation field present')
check('bool flight = HighLogic.LoadedSceneIsFlight;' in preload,
      'current KSP scene is sole UpdatePoints Flight authority')
check('deferredPointSetInvalidation = !string.Equals(' in preload,
      'Flight point churn records only applied-signature difference')
check('points.Clear();' in preload and 'points.AddRange(next);' in preload,
      'latest point snapshot is retained during Flight')
check('void ApplyPointSetInvalidationLocked(string signature)' in preload,
      'single point invalidation implementation exists')
check('appliedPointSetSignature = signature ?? string.Empty;' in preload,
      'successful invalidation advances applied signature')
check('string.Equals(appliedPointSetSignature, pointSetSignature' in preload,
      'non-Flight refresh can clear reverted Flight churn without rebuild')
check('ApplyPointSetInvalidationLocked(pointSetSignature);' in preload,
      'non-Flight changed point-set invalidates through one helper')
check('ApplyDeferredPointSetInvalidation' not in preload,
      'stale first non-Flight Tick cannot apply last Flight point-set')
check('flightSuspended || HighLogic.LoadedSceneIsFlight' not in preload,
      'stale flightSuspended latch is excluded from scene classification')

update_start = preload.find('internal void UpdatePoints(')
helper_start = preload.find('void ApplyPointSetInvalidationLocked(string signature)', update_start)
compare_start = preload.find('static int ComparePreloadPoints', helper_start)
update_slice = preload[update_start:helper_start] if update_start >= 0 and helper_start > update_start else ''
helper_slice = preload[helper_start:compare_start] if helper_start >= 0 and compare_start > helper_start else ''
check(update_start >= 0 and helper_start > update_start,
      'UpdatePoints method boundary resolves before invalidation helper')
check('InvalidateAutomaticCompletion(plan);' not in update_slice,
      'UpdatePoints never directly revokes completion')
check('ApplyPointSetInvalidationLocked(pointSetSignature);' in update_slice,
      'UpdatePoints uses coalesced non-Flight invalidation helper')
check(helper_slice.count('InvalidateAutomaticCompletion(plan);') == 1,
      'single helper owns exactly one automatic completion invalidation call')
flight_start = update_slice.find('if (flight)')
flight_end = update_slice.find('// A non-Flight registry refresh', flight_start)
flight_slice = update_slice[flight_start:flight_end] if flight_start >= 0 and flight_end > flight_start else ''
check('stateDirty' not in flight_slice,
      'Flight point churn remains RAM-only and requests no state-file write')

standby_start = nav.find('static void DrawTerrainStandbyBackground(Rect rect)')
standby_end = nav.find('static void DrawCleanBackground(Rect rect)', standby_start)
standby_slice = nav[standby_start:standby_end] if standby_start >= 0 and standby_end > standby_start else ''
check('new Color(0.025f, 0.145f, 0.285f, 1f)' in standby_slice,
      'ordinary historical blue standby remains unchanged outside R012 cold-init')
check('bool terrainPresentationRequested = settings == null ||' in nav and
      'settings.TerrainGpuMode != AERISTerrainGpuMode.Off' in nav and
      'settings.PerformanceGpuAccelerationEnabled' in nav,
      'cold-init requires terrain GPU presentation to be requested')
check('bool solidBodyColdInit = !hazardOnly && terrainPresentationRequested &&' in nav,
      'pre-render cold-init gate is explicit and excludes hazard-only path')
check('!tileSystem.BodySupported' in nav and
      'AERISTerrainTileSystem.BodyHasSolidSurface(vessel.mainBody)' in nav,
      'cold-init gate distinguishes expected solid terrain from unsupported body')
check('"RELOADING ND\\nTERRAIN INIT"' in nav,
      'pre-render cold-init has unique explicit reload presentation')
cold_start = nav.find('if (solidBodyColdInit)')
cold_end = nav.find('AERISTerrainGpuDrawState gpuState', cold_start)
cold_slice = nav[cold_start:cold_end] if cold_start >= 0 and cold_end > cold_start else ''
check('DrawCleanBackground(plot);' in cold_slice,
      'only R012 cold-init explicitly overwrites standby with clean black background')
check(cold_start >= 0 and cold_start < nav.find('terrainTileRenderer.Draw('),
      'cold-init presentation occurs before renderer admission')
check('"TERRAIN GPU BUILDING " + percent + "%"' in nav,
      'ordinary renderer Partial/BUILDING presentation remains unchanged')
check('bool ndReloading =' in nav and 'terrainTileRenderer.Reloading' in nav,
      'inherited AERIS24 renderer black-reload UI remains present')

for token in ('ndReloadGeneration++;', 'frontReloadGeneration = ndReloadGeneration;',
              'if (Reloading) return false;', 'oh_nd_reload='):
    check(token in renderer, 'AERIS24 black-reload renderer contract retained: ' + token)

check('REV3_5_R012_VARIANT="' + R012 + '"' in build,
      'R012 build identity variable')
check('rev3_5_r012_variant=%s' in build,
      'R012 candidate identity append')
check('verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py' in build,
      'R012 verifier wired into build')
check('selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py' in prebuild,
      'R012 selftest wired into prebuild')
check('FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed 10 Hz presentation authority retained')
check('160000f' in settings, 'exact 160 km range authority retained')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in preload and forbidden not in nav,
          'R012 modified source excludes ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS')
