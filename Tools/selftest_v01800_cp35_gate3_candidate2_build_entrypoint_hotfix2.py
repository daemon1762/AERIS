#!/usr/bin/env python3
import sys
from pathlib import Path
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 3 Candidate 2 Presentation Authority Hotfix 1 Build Entrypoint Hotfix 2')
build=read(ROOT/'build_ubuntu.sh')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
prebuild=read(ROOT/'Tools/run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_prebuild.py')
full=read(ROOT/'Tools/run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_acceptance.py')
identity='DEV CP3.5 GATE 3 — CP3 FROZEN VISUAL PATH RECOVERY / BOUNDED EXACT REFINEMENT CANDIDATE 2 — PRESENTATION AUTHORITY HOTFIX 1 — BUILD ENTRYPOINT HOTFIX 2'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated Build Entrypoint Hotfix 2 identity exact')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build generator Build Entrypoint Hotfix 2 identity exact')
suite.check('PRESENTATION AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 2' in build,'build display identity includes Build Entrypoint Hotfix 2')
suite.check('Presentation Authority Hotfix 1 Build Entrypoint Hotfix 2' in avc,'AVC identity includes Build Entrypoint Hotfix 2')
quick='run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_prebuild.py'
full_name='run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_acceptance.py'
suite.check(quick in build,'normal build invokes lightweight prebuild runner')
suite.check(full_name not in '\n'.join(line for line in build.splitlines() if line.strip() and not line.lstrip().startswith('#')),'normal build does not invoke full manifest acceptance')
active='\n'.join(line for line in build.splitlines() if line.strip() and not line.lstrip().startswith('#'))
suite.check('verify_cp35_gate3_candidate2_presentation_authority_hotfix1_source_manifest.py' not in active,'normal build does not invoke old source manifest verifier')
suite.check('verify_cp35_gate3_candidate2_presentation_authority_hotfix1_package_manifest.py' not in active,'normal build does not invoke old package manifest verifier')
suite.check('SOURCE_MANIFEST_SHA256.txt' not in active and 'MANIFEST_SHA256.txt' not in active,'normal build does not hash package manifests')
suite.check("('Source SHA-256 manifest verification'" not in prebuild and "('Package SHA-256 manifest verification'" not in prebuild,'prebuild runner excludes both manifest suites')
for token in (
 'selftest_v01800_cp35_gate3_cp3_frozen_visual_path_recovery_candidate2.py',
 'selftest_v01800_cp35_gate3_candidate2_presentation_authority_hotfix1.py',
 'selftest_v01800_cp35_gate3_candidate2_build_entrypoint_hotfix2.py',
 'selftest_v01800_cp2_csharp_compile_regression.py'):
    suite.check(token in prebuild,'lightweight prebuild includes '+token)
suite.check('verify_cp35_gate3_candidate2_build_entrypoint_hotfix2_source_manifest.py' in full,'explicit full acceptance includes source manifest')
suite.check('verify_cp35_gate3_candidate2_build_entrypoint_hotfix2_package_manifest.py' in full,'explicit full acceptance includes package manifest')
suite.check('xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj' in build,'xbuild remains native compile authority')
suite.check(build.index(quick) < build.index('xbuild /p:Configuration=Release'),'lightweight prebuild runs before xbuild')
suite.check('cp -f bin/Release/AERISFlightControl.dll' in build,'native DLL install path retained')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE3_CANDIDATE2_PRESENTATION_AUTHORITY_HOTFIX1_BUILD_ENTRYPOINT_HOTFIX2.txt').is_file(),'Build Entrypoint Hotfix 2 acceptance contract included')
suite.check((ROOT/'Docs/CP3.5_GATE3_CANDIDATE2_PRESENTATION_AUTHORITY_BUILD_ENTRYPOINT_HOTFIX2_v0.18.0.0_ja.md').is_file(),'Build Entrypoint Hotfix 2 design note included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE3_CANDIDATE2_PRESENTATION_AUTHORITY_BUILD_ENTRYPOINT_HOTFIX2_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Build Entrypoint Hotfix 2 test card included')
suite.finish()
