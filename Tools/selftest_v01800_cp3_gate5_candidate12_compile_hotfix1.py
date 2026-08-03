#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from v01700_testlib import CheckSuite
suite=CheckSuite("v0.18.0.0 CP3 Gate 5 Candidate 12 Compile Hotfix 1")
version=(ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs').read_text(encoding='utf-8')
build=(ROOT/'build_ubuntu.sh').read_text(encoding='utf-8')
avc=(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text(encoding='utf-8')
runner=(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py').read_text(encoding='utf-8')
c11=(ROOT/'Tools/selftest_v01800_cp3_gate5_candidate11_dlc_placeholder_manual_ab_hotfix1.py').read_text(encoding='utf-8')
c12=(ROOT/'Tools/selftest_v01800_cp3_gate5_candidate12_dlc_dessert_field_verified_default_baseline.py').read_text(encoding='utf-8')
lineage='CANDIDATE 12 DLC DESSERT FIELD VERIFIED DEFAULT BASELINE COMPILE HOTFIX 1'
suite.check(lineage in version and lineage in build,'Compile Hotfix 1 lineage retained in successor build')
suite.check('Candidate 12 DLC Dessert Field-Verified Default Baseline Compile Hotfix 1' in avc,'Compile Hotfix 1 AVC identity')
suite.check('Candidate 11 lineage identity retained in successor build' in c11,'Candidate 11 regression no longer pins current identity')
suite.check('Candidate 12 baseline identity retained in successor build' in c12,'Candidate 12 baseline regression no longer pins current identity')
suite.check('Candidate 11 tab/build identity exact' not in c11,'obsolete Candidate 11 exact-current identity assertion removed')
suite.check('Candidate 12 in-game/build identity exact' not in c12,'obsolete Candidate 12 exact-current identity assertion removed')
suite.check('selftest_v01800_cp3_gate5_candidate12_compile_hotfix1.py' in runner,'Gate 5 runner executes Compile Hotfix 1 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE_COMPILE_HOTFIX1.txt').is_file(),'Compile Hotfix 1 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE_COMPILE_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Compile Hotfix 1 test card present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,'build_ubuntu.sh executable bit retained')
suite.finish()
