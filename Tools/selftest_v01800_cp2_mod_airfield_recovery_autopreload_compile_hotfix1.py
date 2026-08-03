#!/usr/bin/env python3
import re, sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite('v0.18.0.0 CP2 mod-airfield recovery / auto-preload compile hotfix 1')
path = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
source = read(path)

suite.check('using AERISFlightControl.Logging;' in source,
            'preload builder imports the project logging namespace')
suite.equal(source.count('AERISLogger.Info("[PRELOAD_AUTO]'), 2,
            'both automatic-progression events use AERISLogger.Info')
suite.check(not re.search(r'\bAERISLog\s*\.', source),
            'undefined legacy AERISLog identifier is absent')
suite.check('[PRELOAD_AUTO]' in source and 'event=COMPLETE' in source and
            'event=PROMOTE' in source,
            'compile fix preserves both auto-progression telemetry events')

build = read(ROOT / 'build_ubuntu.sh')
generated = read(ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
identity = 'MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1'
suite.check(identity in build and identity in generated,
            'native build identity exposes compile hotfix 1')

for rel in (
    'Docs/CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_COMPILE_HOTFIX_1_v0.18.0.0_ja.md',
    'Docs/ND_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_COMPILE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md',
    'Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_COMPILE_HOTFIX1_ja.md',
    'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MOD_AIRFIELD_RECOVERY_AUTOPRELOAD_COMPILE_HOTFIX1.txt'):
    suite.check((ROOT / rel).is_file(), 'compile hotfix document exists: ' + rel)

suite.finish()
