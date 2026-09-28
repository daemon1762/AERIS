#!/usr/bin/env python3
from pathlib import Path
import re, sys
root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
srcp = root/'Source/AERISFlightControl/Core/AERISExternalAutomationManager.cs'
v2p = root/'Source/AERISFlightControl/Core/AERISExternalAutomationManager.V2.cs'
src = srcp.read_text(encoding='utf-8', errors='replace')
v2 = v2p.read_text(encoding='utf-8', errors='replace')

def method(text, name):
    # Find method declaration and balance braces.
    m = re.search(r'^[ \t]*(?:internal|private|public|static|sealed|partial|virtual|override|async|\s)*[\w<>\[\],.?]+\s+'+re.escape(name)+r'\s*\([^)]*\)\s*\{', text, re.M)
    if not m:
        return ''
    i = text.find('{', m.start())
    depth = 0
    for j in range(i, len(text)):
        c = text[j]
        if c == '{': depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: return text[m.start():j+1]
    return ''

release = method(src, 'ReleaseControl')
safe = method(src, 'EnterSafeHold')
suspend = method(src, 'SuspendForPilot')
expire = method(src, 'Expire')
tick = method(src, 'Tick')
complete = method(src, 'CompleteMission')
failv2 = method(v2, 'FailV2Mission')

checks = []
def ck(name, cond): checks.append((name, bool(cond)))

ck('ReleaseControl has explicit allowSafeHold policy', re.search(r'ReleaseControl\s*\([^)]*bool\s+allowSafeHold', release))
ck('ReleaseControl only enters safe hold when allowSafeHold is true', re.search(r'allowSafeHold[\s\S]{0,240}EnterSafeHold', release))
ck('ReleaseControl requires same leased vessel before safe hold', re.search(r'Session\.VesselId\s*==\s*vessel\.id|vessel\.id\s*==\s*record\.Session\.VesselId', release))
ck('EnterSafeHold receives session record', re.search(r'EnterSafeHold\s*\(\s*SessionRecord\s+record', safe))
ck('EnterSafeHold independently rejects wrong vessel', re.search(r'Session\.VesselId\s*==\s*vessel\.id|vessel\.id\s*==\s*record\.Session\.VesselId', safe))
ck('Pilot termination explicitly forbids safe hold', re.search(r'Terminate\s*\(record,[\s\S]{0,240}SuspendedByPilot[\s\S]{0,260}false\s*,\s*false\s*,\s*false\s*\)', suspend))
ck('Pilot termination drops MASTER before terminal release', re.search(r'core\.Master\s*\)\s*core\.Master\s*=\s*false', suspend))
ck('Wrong-vessel termination explicitly forbids safe hold', re.search(r'WrongVessel[\s\S]{0,180}"ACTIVE VESSEL CHANGED"\s*,\s*false\s*,\s*false\s*,\s*false\s*\)', tick))
ck('Lease expiry explicitly permits same-vessel safe hold', re.search(r'LeaseExpired[\s\S]{0,220}"LEASE EXPIRED[^\n]*"\s*,\s*false\s*,\s*false\s*,\s*true\s*\)', expire))
ck('V2 fault retains same-vessel safe hold', re.search(r'ReleaseControl\s*\(record,\s*detail,\s*false\s*,\s*true\s*\)', failv2))
ck('Ground completion never asks for safe hold', re.search(r'ReleaseControl\s*\(record,\s*detail,\s*false\s*,\s*false\s*\)', complete))
ck('ReleaseControl clears authority state after release', 'record.OwnsControl = false;' in release and 'record.BeforeMission = null;' in release)

print('=== AERIS53 GATE3 R-CORE-01 TARGETED ===')
failed = False
for n, ok in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + n)
    failed |= not ok
print(f'\n{sum(ok for _,ok in checks)}/{len(checks)} PASS')
sys.exit(1 if failed else 0)
