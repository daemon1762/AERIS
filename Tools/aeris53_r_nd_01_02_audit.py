#!/usr/bin/env python3
"""Temporary separate ND/FDI auditor; fault scenarios use renamed private copies."""
from pathlib import Path
import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
GAME = Path.home()/'.steam/debian-installation/steamapps/common/Kerbal Space Program'
TARGET = GAME/'GameData/AERIS53NdBatchAudit'
REPORTS = GAME/'Logs/AERIS53_ND0102'
RETIREMENT = Path.home()/'.cache/AERIS/nd0102-audit-retired'
SOURCES = ['Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs',
           'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs',
           'Source/AERISFlightControl/UI/AERISFlightInstrument.cs']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_fixture(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT/path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def isolated_sources(folder):
    traffic = load_fixture('traffic_fixture', 'TESTS/aeris53_r_nd_01_traffic_clear.py')
    labels = load_fixture('label_fixture', 'TESTS/aeris53_r_nd_02_label_objects.py')
    texts = {'TrafficFixture.cs': traffic.fixture_source(), 'LabelFixture.cs': labels.fixture_source()}
    texts.update({p.name: p.read_text() for p in traffic.production_sources()})
    paths = []
    for name, content in texts.items():
        path = folder/name
        path.write_text(content.replace('AERISFlightControl', 'AERIS53NdBatchIsolated'))
        paths.append(str(path))
    return paths


def install(retry=False):
    previous = None
    if TARGET.exists() and not retry:
        raise SystemExit(f'STOP: audit directory already exists: {TARGET}')
    if retry:
        previous = json.loads((TARGET/'audit-install.json').read_text())
        old_dll = TARGET/'AERIS53NdBatchAudit.dll'
        if digest(old_dll) != previous['audit_sha256']:
            raise SystemExit('STOP: existing auditor identity changed; retained')
        reports = [p for p in REPORTS.glob('*.txt') if p.stat().st_mtime_ns >= previous['installed_ns']]
        if not reports or 'AUDIT_ERROR=' not in max(reports, key=lambda p: p.stat().st_mtime_ns).read_text():
            raise SystemExit('STOP: retry requires a failed runtime report; use collect for a completed audit')
    dlls = list((GAME/'GameData/AERISFlightControl').rglob('AERISFlightControl.dll'))
    if len(dlls) != 1:
        raise SystemExit('STOP: expected exactly one production DLL')
    dll = dlls[0]
    if previous and (Path(previous['production_dll']).resolve() != dll.resolve() or
                     digest(dll) != previous['production_sha256'] or
                     any(digest(ROOT/p) != sha for p, sha in previous['sources'].items())):
        raise SystemExit('STOP: production identity/source changed since failed audit; retained')
    sdk = GAME/'KSP_x64_Data/Managed'
    references = [sdk/name for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll',
                  'Assembly-CSharp.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll']]
    missing = [str(p) for p in references if not p.is_file()]
    if missing:
        raise SystemExit('STOP: missing KSP managed reference(s): '+', '.join(missing))
    with tempfile.TemporaryDirectory(prefix='aeris-nd-audit-') as td:
        folder = Path(td)
        output = folder/'AERIS53NdBatchAudit.dll'
        inputs = isolated_sources(folder)
        subprocess.run(['mcs', '-nostdlib', '-target:library', '-define:AUDIT_LIB', '-warn:0', '-out:'+str(output),
                        *['-r:'+str(p) for p in references], *inputs,
                        str(ROOT/'TESTS/Runtime/AERIS53NdBatchProbe.cs')], check=True)
        backup = None
        if previous:
            backup = RETIREMENT/('failed-'+str(time.time_ns()))
            backup.mkdir(parents=True)
            shutil.move(str(TARGET), str(backup/TARGET.name))
        created = False
        try:
            TARGET.mkdir()
            created = True
            shutil.copy2(output, TARGET/output.name)
            (TARGET/'audit-install.json').write_text(json.dumps({'installed_ns': time.time_ns(),
                'head': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                'production_dll': str(dll), 'production_sha256': digest(dll),
                'sources': {p: digest(ROOT/p) for p in SOURCES},
                'audit_sha256': digest(TARGET/output.name)}, indent=2)+'\n')
        except Exception:
            if created:
                shutil.rmtree(TARGET)
            if backup:
                shutil.move(str(backup/TARGET.name), str(TARGET))
            raise
    if previous:
        print('FAILED_AUDITOR_PRESERVED='+str(backup))
    print('TEMPORARY ND/FDI AUDITOR INSTALLED; audit installation leaves production DLL unchanged.')
    print('KSPでNDとFDIを表示する。TRAFFICをOFFで3秒待ってON、NDをOFFで3秒待って再表示。文字の見た目・サイズ変更・通常飛行を確認し、KSP終了後に collect を実行。')


def collect():
    manifest = json.loads((TARGET/'audit-install.json').read_text())
    if any(digest(ROOT/p) != sha for p, sha in manifest['sources'].items()):
        raise SystemExit('STOP: source changed since audit installation; auditor retained')
    reports = [p for p in REPORTS.glob('*.txt') if p.stat().st_mtime_ns >= manifest['installed_ns']]
    if not reports:
        raise SystemExit('INCONCLUSIVE: no current runtime report; auditor retained')
    report = max(reports, key=lambda p: p.stat().st_mtime_ns)
    content = report.read_text()
    print(content, end='')
    required = ['ISOLATED_RESULT=20/20 PASS\n', 'TRAFFIC_OFF_ALERT_ABSENT=True\n',
                'ND_OFF_ALERT_ABSENT=True\n', 'TRAFFIC_ON_AGAIN=True; ND_ON_AGAIN=True; LABEL_STYLES_READY=True\n',
                'LIVE_RESULT=PASS\n', 'LIVE_DLL_SHA256='+manifest['production_sha256']+'\n']
    if any(s not in content for s in required) or 'AUDIT_ERROR=' in content:
        raise SystemExit('INCONCLUSIVE/FAIL: incomplete live/isolated audit; auditor retained')
    if digest(Path(manifest['production_dll'])) != manifest['production_sha256']:
        raise SystemExit('STOP: installed production identity changed; auditor retained')
    backup = RETIREMENT/str(time.time_ns())
    backup.mkdir(parents=True)
    shutil.copy2(report, backup/report.name)
    (backup/'collection.json').write_text(json.dumps({'result': 'PASS', 'report_sha256': digest(report),
        'installation': manifest}, indent=2)+'\n')
    shutil.move(str(TARGET), str(backup/TARGET.name))
    print('ND_BATCH_AUDIT_RESULT=PASS')
    print('AUDITOR_RETIRED='+str(backup))
    print('監査ソース・ツールを撤去してから、通常確認を行う。')


if __name__ == '__main__':
    if sys.argv[1:] == ['collect']:
        collect()
    elif sys.argv[1:] == ['retry']:
        install(retry=True)
    elif not sys.argv[1:]:
        install()
    else:
        raise SystemExit('usage: aeris53_r_nd_01_02_audit.py [collect|retry]')
