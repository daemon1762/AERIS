#!/usr/bin/env python3
from pathlib import Path
import ast,base64,hashlib,re,subprocess,sys,zlib
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
ORIGINAL=ROOT/'Tools/apply_aeris32_rev3_5_r034_pqslandcontrol_height_path_audit.py'
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR034PqsLandControlHeightPathAuditObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R033_PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW'
MARKER='AERIS32_REV3_5_R034_PQS_LANDCONTROL_HEIGHT_PATH_AUDIT_SHADOW'
EXPECTED_SHA='12cee240c98182e93e02db5c521b231d5cd1952525fac0b440a0fc7e4af452b8'
ALPHABET='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~'

def read_constant(name):
    tree=ast.parse(ORIGINAL.read_text())
    for node in tree.body:
        if not isinstance(node,ast.Assign) or len(node.targets)!=1: continue
        target=node.targets[0]
        if isinstance(target,ast.Name) and target.id==name and isinstance(node.value,ast.Constant):
            return node.value.value
    raise SystemExit('R034 Hotfix1 cannot find '+name+' in original applicator')

def accept(payload,label):
    try:
        raw=base64.b85decode(payload.encode())
        observer=zlib.decompress(raw).decode()
    except Exception:
        return None
    if hashlib.sha256(observer.encode()).hexdigest()!=EXPECTED_SHA:
        return None
    print('PASS R034 payload '+label+'; observer SHA256 exact')
    return observer

payload=read_constant('PAYLOAD')
observer=accept(payload,'direct')
if observer is None:
    overflow_start=None
    try:
        base64.b85decode(payload.encode())
    except ValueError as ex:
        m=re.search(r'base85 overflow in hunk starting at byte (\d+)',str(ex))
        if m: overflow_start=int(m.group(1))
    if overflow_start is None:
        raise SystemExit('R034 Hotfix1: payload is invalid but no bounded Base85 overflow hunk was identified')
    print('R034 Hotfix1: repairing only overflow hunk byte '+str(overflow_start)+'..'+str(min(len(payload)-1,overflow_start+4))+' using fixed observer SHA256 oracle')
    found=[]
    end=min(len(payload),overflow_start+5)
    for pos in range(overflow_start,end):
        original=payload[pos]
        for ch in ALPHABET:
            if ch==original: continue
            candidate=payload[:pos]+ch+payload[pos+1:]
            decoded=accept(candidate,'repair pos='+str(pos)+' '+repr(original)+'->'+repr(ch))
            if decoded is not None:
                found.append((candidate,decoded,pos,original,ch))
                if len(found)>1:
                    raise SystemExit('R034 Hotfix1: ambiguous payload repair; refusing to materialize observer')
    if len(found)!=1:
        raise SystemExit('R034 Hotfix1: no unique one-character repair matched fixed observer SHA256')
    payload,observer,pos,old,new=found[0]
    print('R034 Hotfix1: UNIQUE_REPAIR pos='+str(pos)+' old='+repr(old)+' new='+repr(new))

SOURCE.parent.mkdir(parents=True,exist_ok=True)
if SOURCE.exists() and SOURCE.read_text()!=observer:
    raise SystemExit('R034 LandControl observer exists with unexpected content')
SOURCE.write_text(observer)
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
print('PASS apply R034 PQSLandControl height-path audit shadow Hotfix1')
print('observer_sha256='+EXPECTED_SHA)
print('head='+head)
