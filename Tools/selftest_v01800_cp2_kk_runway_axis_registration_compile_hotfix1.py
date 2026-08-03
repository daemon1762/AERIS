#!/usr/bin/env python3
import re, sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read
suite = CheckSuite('v0.18.0.0 CP2 KK runway axis registration compile hotfix 1')
path = ROOT / 'Source/AERISFlightControl/Landing/AERISRunwayGeometryWorker.cs'
source = read(path)
segment = source[source.find('static bool TryRunwaySurfacePca'):source.find('static void AddHeadingCandidate')]
suite.check('double primitiveAspect =' in segment,
            'primitive-loop aspect uses a scope-unique identifier')
suite.check('TrustedRunwayAxisPrimitive(snapshot, primitive, primitiveAspect)' in segment,
            'primitive aspect identifier is consumed by the trust filter')
suite.check('double east, north, surfaceAspect;' in segment,
            'refined PCA aspect uses a scope-unique identifier')
suite.check('out surfaceAspect' in segment and 'surfaceAspect < 4.0' in segment,
            'refined PCA aspect identifier is consistently consumed')
suite.check(not re.search(r'\bdouble\s+aspect\s*=', segment),
            'legacy local variable name aspect is absent from the affected method')
build = read(ROOT / 'build_ubuntu.sh')
generated = read(ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
identity = 'AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1'
suite.check(identity in build and identity in generated,
            'build identity exposes compile hotfix 1')
for rel in (
    'Docs/CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX_1_v0.18.0.0_ja.md',
    'Docs/ND_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md',
    'Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX1_ja.md',
    'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX1.txt'):
    suite.check((ROOT / rel).is_file(), 'compile hotfix document exists: ' + rel)
suite.finish()
