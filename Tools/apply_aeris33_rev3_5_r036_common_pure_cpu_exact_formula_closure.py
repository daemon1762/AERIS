#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R035_LANDCONTROL_WRITE_SEMANTICS_IL_AUDIT_SHADOW'
MARKER='AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW'
EXPECTED_SHA='4e045e0123a493179fe986bd9ed746d0732e06f5e8eace38006ff0571ea3ee7f'

if not SOURCE.is_file(): raise SystemExit('R036 tracked observer missing')
actual=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
if actual!=EXPECTED_SHA: raise SystemExit('R036 tracked observer SHA256 mismatch expected='+EXPECTED_SHA+' actual='+actual)
print('PASS R036 tracked plain-C# observer SHA256 exact')

cs=CSPROJ.read_text()
inc='    <Compile Include="Terrain\\AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR035LandControlWriteSemanticsIlAuditObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs: raise SystemExit('R035 observer csproj anchor missing; materialize accepted R035 first')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))

version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R035 parent identity missing; materialize accepted R035 first')
version=version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R035 LANDCONTROL WRITE SEMANTICS IL AUDIT SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS33 REV3.5 R036 COMMON PURE CPU EXACT FORMULA CLOSURE SHADOW'
).replace(PARENT,MARKER)

head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip()
h=hashlib.sha256()
files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for p in files:
    if p==VERSION: continue
    h.update(str(p.relative_to(ROOT)).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
               'internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
               'internal const string SourceTreeSha256 = "'+h.hexdigest()+'";',version)
VERSION.write_text(version)
print('PASS apply R036 common pure CPU exact formula closure shadow')
print('observer_sha256='+EXPECTED_SHA)
print('head='+head)
