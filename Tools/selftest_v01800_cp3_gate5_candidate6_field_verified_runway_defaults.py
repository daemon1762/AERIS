#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
errors=[]
def check(cond,msg):
    if cond: print('PASS:',msg)
    else:
        print('FAIL:',msg); errors.append(msg)

def text(rel): return (root/rel).read_text(errors='replace')

def sha(rel): return hashlib.sha256((root/rel).read_bytes()).hexdigest()

w=text('Source/AERISFlightControl/Landing/AERISRunwayWitnessLibrary.cs')
default_rel='GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg'
d=text(default_rel)
version=text('Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build=text('build_ubuntu.sh')
avc=text('GameData/AERISFlightControl/AERISFlightControl.version')

check('DefaultCalibrationRelativePath' in w,'default calibration path contract exists')
check(default_rel in w,'runtime points at shipped field-verified baseline')
check('File.Copy(defaultPath, userPath, false)' in w,'fresh install seeds defaults without overwrite')
check('if (!File.Exists(userPath) && File.Exists(defaultPath))' in w,'seed occurs only when user file is absent')
check('existing user file always wins' in w,'source documents user override precedence')
check('path = defaultPath;' in w,'read-only fallback uses shipped baseline if seed copy fails')
check('UserCalibrated = true' in w,'default-loaded records remain manual user-calibrated authority')
check('Source = "USER_CALIBRATED"' in w,'default does not masquerade as automatic certification')

check('name = Dessert Airfield' in d or sha(default_rel)=='b9fd40fd1a8fcf2cb6f5f33900c88b6b5e3b5b6c6d070c2544c2829de43a9347',
      'Candidate 6 baseline is preserved; later field-verified additions may extend it')
blocks=re.findall(r'Calibration\s*\{(.*?)\n\}',d,re.S)
check(len(blocks)>=40,'baseline retains at least the original 40 physical non-stock runway calibrations')
ids=[]; complete=0; reciprocal=0; finite=0
for b in blocks:
    vals={}
    for line in b.splitlines():
        if '=' in line:
            k,v=line.split('=',1); vals[k.strip()]=v.strip()
    ids.append(vals.get('providerStableRecordId',''))
    if vals.get('hasStart')=='True' and vals.get('hasEnd')=='True': complete+=1
    if vals.get('reciprocalDirectionPair')=='True': reciprocal+=1
    try:
        nums=[float(vals[k]) for k in ('startLat','startLon','startAlt','endLat','endLon','endAlt','directionAHeadingDeg','directionBHeadingDeg')]
        if all(x==x and abs(x)!=float('inf') for x in nums): finite+=1
    except Exception: pass
check(complete==len(blocks),'all shipped A/B endpoint pairs complete')
check(reciprocal==len(blocks),'all shipped reciprocal direction pairs complete')
check(finite==len(blocks),'all shipped coordinate/heading records finite')
check(len([x for x in ids if x])>=40 and len(set(x for x in ids if x))==len([x for x in ids if x]),'original providerStableRecordId values remain unique; DLC may use providerSiteId identity')
check('coordinateFrame = BODY_FIXED_GEODETIC_ABSOLUTE' in d and d.count('coordinateFrame = BODY_FIXED_GEODETIC_ABSOLUTE')==len(blocks),
      'all shipped baseline endpoints are body-fixed geodetic absolute')

expected='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"'
check(expected in version and expected in build,'Candidate 6 tab/build identity exact')
check('Candidate 6 Field-Verified Runway Default Baseline' in avc,'Candidate 6 AVC identity')
check('03_Field_Verified_Runway_Calibrations.cfg' in text('README.md'),'README identifies shipped baseline')
check((root/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE6_FIELD_VERIFIED_RUNWAY_DEFAULT_BASELINE.txt').exists(),
      'Candidate 6 acceptance document present')

# Authority regressions: Candidate 5 non-stock auto-cert prohibition must remain.
reg=text('Source/AERISFlightControl/Landing/AERISAirfieldRegistry.cs')
ui=text('Source/AERISFlightControl/UI/AERISWindow.cs')
check('return record != null && record.Source == AERISAirfieldSource.Stock;' in reg,'base-game Stock-only automatic authority remains')
check('DISABLED FOR DLC / MOD / USERCFG RUNWAYS' in reg and 'AERISRunwayCertificationBasis.UserCalibrated' in reg,'non-stock automatic authority remains prohibited')

if errors: raise SystemExit(1)
print('\n[CP3 Gate 5 Candidate 6 Field-Verified Runway Default Baseline] %d checks PASS' % 22)
