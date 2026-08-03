#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from v01700_testlib import CheckSuite
suite=CheckSuite("v0.18.0.0 CP3 Gate 5 Candidate 12 Compile Hotfix 2")
version=(ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs').read_text(encoding='utf-8')
build=(ROOT/'build_ubuntu.sh').read_text(encoding='utf-8')
avc=(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text(encoding='utf-8')
runner=(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py').read_text(encoding='utf-8')
c11=(ROOT/'Tools/selftest_v01800_cp3_gate5_candidate11_dlc_placeholder_manual_ab_hotfix1.py').read_text(encoding='utf-8')
c12h1=(ROOT/'Tools/selftest_v01800_cp3_gate5_candidate12_compile_hotfix1.py').read_text(encoding='utf-8')
identity='CANDIDATE 12 DLC DESSERT FIELD VERIFIED DEFAULT BASELINE COMPILE HOTFIX 2'
normalized_c11='CANDIDATE 11 DLC PLACEHOLDER MANUAL A B HOTFIX 1'
decorated_c11='Historical Candidate 11 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 11 — DLC PLACEHOLDER MANUAL A/B HOTFIX 1"'
suite.check(identity in version and identity in build,'Compile Hotfix 2 lineage retained in successor build')
suite.check('Candidate 12 DLC Dessert Field-Verified Default Baseline Compile Hotfix 2' in avc,'Compile Hotfix 2 AVC identity')
suite.check("lineage='CANDIDATE 11 DLC PLACEHOLDER MANUAL A B HOTFIX 1'" in c11 and
            'suite.check(lineage in version and lineage in build' in c11,
            'Candidate 11 regression uses generated-source-stable normalized lineage')
suite.check('CANDIDATE 11 — DLC PLACEHOLDER MANUAL A/B HOTFIX 1' not in c11,
            'Candidate 11 regression no longer depends on decorative generated-source comment')
suite.check(normalized_c11 in version and normalized_c11 in build,
            'Candidate 11 normalized lineage exists before and after version generation')
suite.check(decorated_c11 in version and decorated_c11 in build,
            'Candidate 11 historical marker retained in packaged and generated templates')
suite.check('Compile Hotfix 1 lineage retained in successor build' in c12h1 and 'Compile Hotfix 1 tab/build identity exact' not in c12h1,
            'Compile Hotfix 1 successor-safe contract retained')
suite.check('selftest_v01800_cp3_gate5_candidate12_compile_hotfix2.py' in runner,
            'Gate 5 runner executes Compile Hotfix 2 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE_COMPILE_HOTFIX2.txt').is_file(),
            'Compile Hotfix 2 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE12_DLC_DESSERT_FIELD_VERIFIED_DEFAULT_BASELINE_COMPILE_HOTFIX2_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Compile Hotfix 2 test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE12_COMPILE_HOTFIX2.txt').is_file(),
            'Compile Hotfix 2 source diff evidence present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,'build_ubuntu.sh executable bit retained')
suite.finish()
