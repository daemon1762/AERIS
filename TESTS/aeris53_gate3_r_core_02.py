#!/usr/bin/env python3
from pathlib import Path
import re, sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
apip = root/'Source/AERISFlightControl/API/AERISExternalAutomationApi.cs'
srcp = root/'Source/AERISFlightControl/Core/AERISExternalAutomationManager.cs'
v2p = root/'Source/AERISFlightControl/Core/AERISExternalAutomationManager.V2.cs'
api = apip.read_text(encoding='utf-8', errors='replace')
src = srcp.read_text(encoding='utf-8', errors='replace')
v2 = v2p.read_text(encoding='utf-8', errors='replace')

def method(text, name):
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
            if depth == 0:
                return text[m.start():j+1]
    return ''

submit = method(src, 'TrySubmitSetpointMission')
validate = method(src, 'ValidateSetpoint')
update = method(src, 'UpdateSetpoint')
has_alt = method(src, 'HasAltitudeSetpoint')
has_speed = method(src, 'HasSpeedSetpoint')
normalize = method(src, 'NormalizeSetpointTargets')
corridor = method(v2, 'TryUpdateActiveLearningCorridorSetpoint')
snapshot = method(v2, 'SnapshotSetpointRequest')

checks = []
def ck(name, cond): checks.append((name, bool(cond)))

ck('default request uses omission sentinels for legacy and v2 ALT/VEL fields',
   re.search(r'AltitudeM\s*=\s*int\.MinValue', api) and
   re.search(r'TrueAirspeedMps\s*=\s*float\.NaN', api) and
   re.search(r'AltitudeMeters\s*=\s*double\.NaN', api) and
   re.search(r'SurfaceSpeedMps\s*=\s*double\.NaN', api))

ck('ALT presence is explicit and zero remains representable',
   'int.MinValue' in has_alt and 'AltitudeM' in has_alt)

ck('VEL presence is explicit and zero remains representable',
   'SurfaceSpeedMps' in has_speed and 'Finite' in has_speed)

ck('legacy/v2 aliases are normalized before validation',
   'NormalizeSetpointTargets(request)' in submit and
   'TrueAirspeedMps' in normalize and 'AltitudeMeters' in normalize)

ck('default request fails closed instead of arming ALT/VEL zero',
   re.search(r'!altitudeRequested\s*&&\s*!speedRequested\s*&&\s*!request\.UseExplicitHeading', validate))

ck('zero ALT and VEL remain valid values',
   re.search(r'AltitudeM\s*<\s*0', validate) and
   re.search(r'SurfaceSpeedMps\s*<\s*0', validate) and
   not re.search(r'AltitudeM\s*<=\s*0', validate) and
   not re.search(r'SurfaceSpeedMps\s*<=\s*0', validate))

ck('ALT target/arming only occurs when ALT is present',
   re.search(r'if\s*\(altitudeRequested\)[\s\S]{0,700}core\.Altitude\.TrySetTarget', submit) and
   re.search(r'if\s*\(altitudeRequested\)[\s\S]{0,1100}core\.Altitude\.SetArmed', submit))

ck('VEL target/arming only occurs when VEL is present',
   re.search(r'if\s*\(speedRequested\)[\s\S]{0,700}core\.Velocity\.TrySetTarget', submit) and
   re.search(r'if\s*\(speedRequested\)[\s\S]{0,1100}core\.Velocity\.SetArmed', submit))

ck('runtime pilot-release checks only requested directors',
   re.search(r'altitudeRequested\s*&&\s*!core\.Altitude\.Armed', update) and
   re.search(r'speedRequested\s*&&\s*!core\.Velocity\.Armed', update))

ck('runtime stable test ignores omitted ALT/VEL dimensions',
   re.search(r'!altitudeRequested\s*\|\|\s*altitudeError\s*<=', update) and
   re.search(r'!speedRequested\s*\|\|\s*speedError\s*<=', update))

ck('runtime vertical-speed stability applies only when ALT is requested',
   re.search(r'altitudeRequested\s*&&\s*!Finite\(verticalSpeedSample\)', update) and
   re.search(r'!altitudeRequested\s*\|\|\s*vs\s*<=\s*r\.VerticalSpeedToleranceMps', update))

ck('corridor setpoint update is presence-aware',
   re.search(r'if\s*\(altitudeRequested\)[\s\S]{0,700}core\.Altitude\.TrySetTarget', corridor) and
   re.search(r'if\s*\(speedRequested\)[\s\S]{0,3000}core\.Velocity\.TrySetTarget', corridor))

ck('corridor preserves explicit zero VEL instead of applying a hidden minimum',
   re.search(r'requestedSpeed\s*=\s*Mathf\.Max\(0f,\s*\(float\)request\.SurfaceSpeedMps\)', corridor) and
   'Mathf.Max(10f, (float)request.SurfaceSpeedMps)' not in corridor)

ck('snapshot preserves normalized presence sentinels/values',
   'AltitudeM = value.AltitudeM' in snapshot and
   'SurfaceSpeedMps = value.SurfaceSpeedMps' in snapshot)

print('=== AERIS53 GATE3 R-CORE-02 TARGETED ===')
failed = False
for n, ok in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + n)
    failed |= not ok
print(f'\n{sum(ok for _,ok in checks)}/{len(checks)} PASS')
sys.exit(1 if failed else 0)
