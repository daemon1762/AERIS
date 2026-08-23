#!/usr/bin/env python3
from pathlib import Path
import ast,base64,hashlib,itertools,re,subprocess,sys,zlib
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
    raise SystemExit('R034 Hotfix2 cannot find '+name+' in original applicator')

def decode_observer(raw):
    try:
        observer=zlib.decompress(raw).decode()
    except Exception:
        return None
    if hashlib.sha256(observer.encode()).hexdigest()!=EXPECTED_SHA:
        return None
    return observer

def direct(payload):
    try:
        raw=base64.b85decode(payload.encode())
    except Exception:
        return None
    return decode_observer(raw)

payload=read_constant('PAYLOAD')
observer=direct(payload)
repair_label='direct'
if observer is None:
    overflow_start=None
    try:
        base64.b85decode(payload.encode())
    except ValueError as ex:
        m=re.search(r'base85 overflow in hunk starting at byte (\d+)',str(ex))
        if m: overflow_start=int(m.group(1))
    if overflow_start is None:
        raise SystemExit('R034 Hotfix2: invalid payload but no bounded Base85 overflow hunk identified')
    if overflow_start%5!=0:
        raise SystemExit('R034 Hotfix2: overflow hunk is not Base85-aligned; refusing repair')
    end=overflow_start+5
    if end>len(payload):
        raise SystemExit('R034 Hotfix2: overflow hunk is truncated; refusing repair')
    original_hunk=payload[overflow_start:end]
    print('R034 Hotfix2: overflow hunk byte '+str(overflow_start)+'..'+str(end-1)+' original='+repr(original_hunk))
    try:
        prefix_raw=base64.b85decode(payload[:overflow_start].encode())
    except Exception as ex:
        raise SystemExit('R034 Hotfix2: prefix before overflow hunk is also invalid: '+str(ex))
    try:
        suffix_raw=base64.b85decode(payload[end:].encode())
    except Exception as ex:
        raise SystemExit('R034 Hotfix2: payload has another invalid Base85 hunk after byte '+str(end-1)+': '+str(ex)+'; refusing bounded repair')

    found=[]
    def try_hunk(hunk,label):
        try:
            hraw=base64.b85decode(hunk.encode())
        except Exception:
            return
        decoded=decode_observer(prefix_raw+hraw+suffix_raw)
        if decoded is None:
            return
        found.append((hunk,decoded,label))
        print('PASS R034 payload '+label+'; observer SHA256 exact')
        if len(found)>1:
            raise SystemExit('R034 Hotfix2: ambiguous fixed-SHA repair; refusing to materialize observer')

    print('R034 Hotfix2: pass 1/2 - testing one-character substitutions inside the single overflow hunk')
    for pos in range(5):
        old=original_hunk[pos]
        for ch in ALPHABET:
            if ch==old: continue
            h=original_hunk[:pos]+ch+original_hunk[pos+1:]
            try_hunk(h,'one-char hunk_pos='+str(pos)+' '+repr(old)+'->'+repr(ch))
    if not found:
        print('R034 Hotfix2: pass 2/2 - testing two-character substitutions inside the single overflow hunk')
        for p1,p2 in itertools.combinations(range(5),2):
            o1=original_hunk[p1];o2=original_hunk[p2]
            for c1 in ALPHABET:
                if c1==o1: continue
                for c2 in ALPHABET:
                    if c2==o2: continue
                    chars=list(original_hunk);chars[p1]=c1;chars[p2]=c2
                    try_hunk(''.join(chars),'two-char hunk_pos='+str(p1)+','+str(p2)+' '+repr(o1)+repr(o2)+'->'+repr(c1)+repr(c2))
    if len(found)!=1:
        raise SystemExit('R034 Hotfix2: no unique <=2-character repair in the single overflow hunk matched fixed observer SHA256; payload repack required')
    repaired_hunk,observer,repair_label=found[0]
    print('R034 Hotfix2: UNIQUE_REPAIR original_hunk='+repr(original_hunk)+' repaired_hunk='+repr(repaired_hunk)+' mode='+repair_label)

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
print('PASS apply R034 PQSLandControl height-path audit shadow Hotfix2')
print('observer_sha256='+EXPECTED_SHA)
print('repair='+repair_label)
print('head='+head)
