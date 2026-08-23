#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR034PqsLandControlHeightPathAuditObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R033_PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW'
MARKER='AERIS32_REV3_5_R034_PQS_LANDCONTROL_HEIGHT_PATH_AUDIT_SHADOW'
OBSERVER_SHA256='d6f7b8d87df294d76b29f0946529e2bc8de5a5033261b6f32abc1d2433b5c12a'

def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()

if not SOURCE.is_file():
    raise SystemExit('R034 Hotfix3 tracked observer missing; git pull --ff-only first')
actual=sha(SOURCE)
if actual!=OBSERVER_SHA256:
    raise SystemExit('R034 Hotfix3 tracked observer SHA256 mismatch expected='+OBSERVER_SHA256+' actual='+actual)
print('PASS R034 Hotfix3 tracked plain-C# observer SHA256 exact')

cs=CSPROJ.read_text()
inc='    <Compile Include="Terrain\\AERISR034PqsLandControlHeightPathAuditObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR033PtcPureProceduralDependencyInventoryObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs:
        raise SystemExit('R033 dependency inventory csproj anchor missing; materialize accepted R033 first')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))

version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R033 dependency inventory parent identity missing; materialize accepted R033 first')
version=version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R033 PTC PURE PROCEDURAL DEPENDENCY INVENTORY SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R034 PQS LANDCONTROL HEIGHT PATH AUDIT SHADOW'
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
print('PASS apply R034 PQSLandControl height-path audit shadow Hotfix3 plain-C#')
print('observer_sha256='+OBSERVER_SHA256)
print('head='+head)
