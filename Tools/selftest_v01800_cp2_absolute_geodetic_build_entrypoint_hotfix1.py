#!/usr/bin/env python3
import json
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 absolute geodetic build entrypoint hotfix 1")
build = read(ROOT / "build_ubuntu.sh")
generated = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
version = json.loads((ROOT / "GameData/AERISFlightControl/AERISFlightControl.version").read_text(encoding="utf-8"))
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")
readme = read(ROOT / "README.md")

suffix = "MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1"
old_tail = "MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1\""

match = re.search(r'^DISPLAY="([^"]+)"$', build, re.MULTILINE)
suite.check(match is not None, "build entrypoint declares one generated display identity")
build_display = match.group(1).replace("$SEMVER", "0.18.0.0") if match else ""
match_generated = re.search(r'internal const string Display = "([^"]+)";', generated)
suite.check(match_generated is not None, "generated C# build identity is readable")
generated_display = match_generated.group(1) if match_generated else ""

suite.check(build_display.endswith(suffix),
            "build entrypoint ends in the current absolute-geodetic hotfix identity")
suite.check(generated_display.endswith(suffix),
            "generated C# identity ends in the current build-entrypoint hotfix")
suite.equal(build_display, generated_display,
            "build entrypoint and generated C# identity are byte-identical after semver substitution")
suite.check(old_tail not in build,
            "stale grouping-only build identity cannot overwrite the current generated identity")
name = version.get("NAME", "")
suite.check("Manual Runway Absolute Geodetic Endpoint Authority Hotfix 1" in name and
            "Build Entrypoint Hotfix 1" in name,
            "AVC metadata names the current build-entrypoint hotfix")
suite.check("Absolute Geodetic Endpoint Authority Build Entrypoint Hotfix 1" in readme,
            "README names the current checkpoint")

write_pos = build.find('> "$GEN"')
accept_match = re.search(
    r'^PYTHONDONTWRITEBYTECODE=1 python3 "\$ROOT/Tools/(run_v01800_cp(?:2|3)[^"/]*_acceptance\.py)"$',
    build,
    re.MULTILINE)
accept_pos = accept_match.start() if accept_match else -1
xbuild_pos = build.find('xbuild /p:Configuration=Release')
suite.check(write_pos >= 0 and accept_pos > write_pos,
            "current identity is regenerated before build-time acceptance")
suite.check(xbuild_pos > accept_pos >= 0,
            "native xbuild remains gated by the current acceptance run")
suite.check('selftest_v01800_cp2_absolute_geodetic_build_entrypoint_hotfix1.py' in runner,
            "full CP2 acceptance includes the build-entrypoint regression")
suite.check('MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1' in generated,
            "absolute-geodetic runtime fix remains identified")

for rel in (
    'Docs/CP2_MANUAL_RUNWAY_ABSOLUTE_GEODETIC_ENDPOINT_AUTHORITY_BUILD_ENTRYPOINT_HOTFIX_1_v0.18.0.0_ja.md',
    'Docs/ND_CP2_MANUAL_RUNWAY_ABSOLUTE_GEODETIC_ENDPOINT_AUTHORITY_BUILD_ENTRYPOINT_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md',
    'Evidence/NATIVE_BUILD_ENTRYPOINT_FAILURE_DIAGNOSIS_ABSOLUTE_GEODETIC_HOTFIX1.txt',
    'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_ABSOLUTE_GEODETIC_BUILD_ENTRYPOINT_HOTFIX1.txt'):
    suite.check((ROOT / rel).is_file(), 'current hotfix evidence exists: ' + rel)

suite.finish()
