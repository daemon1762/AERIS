#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
P = (ROOT / 'Tools/prepare_aeris25_main_thread_commit_governor_rev004_runtime.py').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


ck("OH_REVISION = 'OH_PHASE6_004'" in P,
   'rev004 runtime preparer declares exact OH revision')
ck('def assert_generated_rev004_source()' in P and
   'GENERATED SOURCE IDENTITY FAIL' in P and
   'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in P,
   'generated source identity is checked before final build')
ck('def invalidate_parent_runtime_artifacts(ksp)' in P and
   "installed / 'Plugins/AERISFlightControl.dll'" in P and
   "installed / 'AERISCandidateBuildIdentity.txt'" in P,
   'parent rev003 installed DLL and identity are invalidated before final build')
ck("source / 'bin'" in P and "source / 'obj'" in P and
   'shutil.rmtree(path)' in P,
   'rev004 final compile is forced clean by removing bin/obj')
ck('def dll_contains_text(path, text)' in P and
   "text.encode('utf-8')" in P and "text.encode('utf-16le')" in P,
   'DLL identity probe scans both metadata/user-string encodings')
ck("dll_contains_text(installed_dll, OH_REVISION)" in P,
   'installed DLL must embed OH_PHASE6_004')
ck("dll_contains_text(installed_dll, 'oh_managed_prep_submitted=')" in P,
   'installed DLL must embed rev004 managed-prep runtime telemetry')
ck("dll_contains_text(installed_dll, 'REV004 MANAGED PREPARATION PIPELINE')" in P,
   'installed DLL must embed rev004 build display')
ck("identity_value(identity_text, 'built_dll_sha256') == installed_sha" in P,
   'candidate identity built DLL SHA must equal installed DLL SHA')
ck("identity_value(identity_text, 'git') == current_git" in P,
   'candidate identity git value must equal actual HEAD')
ck("if installed_dll.is_file():\n            installed_dll.unlink()" in P and
   'INSTALL IDENTITY FAIL' in P,
   'failed final identity gate removes bad DLL before any KSP test')
ck("print('oh_revision=' + OH_REVISION)" in P,
   'success output reports the verified rev004 revision')

failed = [name for ok, name in checks if not ok]
print('\n[AERIS25 NOREPINEPHRINE REV004 INSTALL IDENTITY HOTFIX] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE REV004 INSTALL IDENTITY HOTFIX] STATIC PASS')
