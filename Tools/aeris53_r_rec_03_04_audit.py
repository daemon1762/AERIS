#!/usr/bin/env python3
"""Temporary desktop auditor installer/collector. Never alters production logs."""
from pathlib import Path
import csv
import hashlib
import importlib.util
import io
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
GAME = Path.home()/'.steam/debian-installation/steamapps/common/Kerbal Space Program'
TARGET = GAME/'GameData/AERIS53RecorderBatchAudit'
MANIFEST = TARGET/'audit-install.json'
REPORTS = GAME/'Logs/AERIS53_REC0304'
RETIREMENT = Path.home()/'.cache/AERIS/rec0304-audit-retired'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def install():
    if TARGET.exists():
        raise SystemExit(f'STOP: existing audit directory: {TARGET}')
    sdk = GAME/'KSP_x64_Data/Managed'
    dll = GAME/'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
    if not dll.is_file() or not (sdk/'Assembly-CSharp.dll').is_file():
        raise SystemExit('STOP: production DLL or KSP SDK not found')
    spec = importlib.util.spec_from_file_location('rec_startup', ROOT/'TESTS/aeris53_r_rec_03_04_startup.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    with tempfile.TemporaryDirectory(prefix='aeris-rec-audit-') as td:
        work = Path(td)
        fixture = work/'IsolatedStartup.cs'
        fixture.write_text(module.fixture_source())
        output = work/'AERIS53RecorderBatchAudit.dll'
        subprocess.run(['mcs', '-target:library', '-define:AUDIT_LIB', '-warn:0', '-out:'+str(output),
                        '-r:'+str(sdk/'Assembly-CSharp.dll'), '-r:'+str(sdk/'UnityEngine.dll'),
                        '-r:'+str(sdk/'UnityEngine.CoreModule.dll'), str(fixture),
                        str(ROOT/'TESTS/Runtime/AERIS53RecorderBatchProbe.cs')], check=True)
        installed_ns = time.time_ns()
        TARGET.mkdir()
        try:
            shutil.copy2(output, TARGET/output.name)
            MANIFEST.write_text(json.dumps({'installed_ns': installed_ns, 'head': subprocess.check_output(
                ['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                'production_dll': str(dll), 'production_sha256': digest(dll),
                'recorder_source_sha256': digest(module.SOURCE),
                'audit_sha256': digest(TARGET/output.name)}, indent=2)+'\n')
        except Exception:
            shutil.rmtree(TARGET)
            raise
    print('TEMPORARY RECORDER AUDITOR INSTALLED; production DLL unchanged.')
    print('KSPを起動し、機体を滑走路に出して15秒待つ。通常の飛行確認後、宇宙センターに戻って5秒待ち、KSPを終了して collect を実行。')


def native_path(value):
    value = value.replace('\\', '/')
    if value.startswith('Z:'):
        value = value[2:]
    return Path(value)


def collect():
    manifest = json.loads(MANIFEST.read_text())
    if digest(ROOT/'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs') != manifest['recorder_source_sha256']:
        raise SystemExit('STOP: recorder source changed since auditor installation; auditor retained')
    reports = [p for p in REPORTS.glob('*.txt') if p.stat().st_mtime_ns >= manifest['installed_ns']]
    if not reports:
        raise SystemExit('INCONCLUSIVE: current installation has no runtime report; auditor retained')
    report = max(reports, key=lambda p: p.stat().st_mtime_ns)
    content = report.read_text()
    print(content, end='')
    if 'ISOLATED_RESULT=15/15 PASS\n' not in content or 'LIVE_RESULT=LIVE_CORE_OBSERVED\n' not in content:
        raise SystemExit('INCONCLUSIVE/FAIL: complete isolated/live observation missing; auditor retained')
    if f"LIVE_DLL_SHA256={manifest['production_sha256']}\n" not in content or \
            digest(Path(manifest['production_dll'])) != manifest['production_sha256']:
        raise SystemExit('STOP: production identity changed; auditor retained')
    folder = native_path(re.findall(r'^LIVE_FOLDER=(.+)$', content, re.M)[-1])
    expected_root = (GAME/'GameData/AERISFlightControl/Logs/FlightData').resolve()
    if folder.resolve().parent != expected_root:
        raise SystemExit('STOP: unexpected flight folder; auditor retained')
    archive = Path(str(folder)+'.zip')
    if not archive.is_file():
        raise SystemExit('INCONCLUSIVE: sealed flight ZIP not found; auditor retained')
    with zipfile.ZipFile(archive) as z:
        bad = z.testzip()
        if bad:
            raise SystemExit(f'FAIL: archive CRC: {bad}; auditor retained')
        source = (ROOT/'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs').read_text()
        for name, writer in [('fdr_flight.csv', 'fdrWriter'), ('cvr_events.csv', 'cvrWriter')]:
            expected = re.search(r'\b'+writer+r'\.WriteLine\("([^"\n]*)"\);', source).group(1).split(',')
            rows = list(csv.reader(io.StringIO(z.read(name).decode('utf-8-sig'))))
            if not rows or rows[0] != expected or len(rows) < 2 or any(len(r) != len(expected) for r in rows):
                raise SystemExit(f'FAIL: {name} schema/data; auditor retained')
            print(f'ZIP_CSV_PASS={name}; columns={len(expected)}; rows={len(rows)-1}')
        unused = ['fdr_bank_diagnostics.csv','fdr_ap_smoothness.csv','fdr_vs_diagnostics.csv',
                  'fdr_vs_cruise_acceleration_guide.csv','fdr_pitch_diagnostics.csv','fdr_hdg_diagnostics.csv',
                  'fdr_alt_diagnostics.csv','fdr_acc_diagnostics.csv','fdr_vel_diagnostics.csv','fdr_ground_takeoff.csv']
        if any(n in z.namelist() for n in unused):
            raise SystemExit('FAIL: disabled diagnostic files present; auditor retained')
        if 'HIGH_RATE_DIAGNOSTICS_ENABLED=False' not in z.read('metadata.txt').decode('utf-8-sig'):
            raise SystemExit('FAIL: metadata policy missing; auditor retained')
        cvr = z.read('cvr_events.csv').decode('utf-8-sig')
        if 'flight recorder started' not in cvr or 'flight recorder stopped' not in cvr:
            raise SystemExit('FAIL: start/end CVR evidence missing; auditor retained')
    backup = RETIREMENT/str(time.time_ns())
    backup.mkdir(parents=True)
    shutil.copy2(report, backup/report.name)
    (backup/'collection.json').write_text(json.dumps({'result': 'PASS', 'zip_path': str(archive),
        'zip_sha256': digest(archive), 'report_sha256': digest(report), 'installation': manifest}, indent=2)+'\n')
    shutil.move(str(TARGET), str(backup/TARGET.name))
    print('ZIP_PATH='+str(archive))
    print('ZIP_SHA256='+digest(archive))
    print('BATCH_AUDIT_RESULT=PASS')
    print('AUDITOR_RETIRED='+str(backup))
    print('次は監査ソース・導入ツールの撤去版で、通常動作を確認する。')


if __name__ == '__main__':
    if sys.argv[1:] == ['collect']:
        collect()
    elif not sys.argv[1:]:
        install()
    else:
        raise SystemExit('usage: aeris53_r_rec_03_04_audit.py [collect]')
