#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR038PtcMinmusVertexPlanetDependencyClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS33_REV3_5_R037_PTC_POL_FLATTEN_OCEAN_EXACT_CLOSURE_SHADOW'
MARKER='AERIS33_REV3_5_R038_PTC_MINMUS_VERTEXPLANET_DEPENDENCY_CLOSURE_SHADOW'
PREFIX='[AERIS33 R038 MINMUS VERTEXPLANET DEPENDENCY CLOSURE APPLY]'

def out(a): return subprocess.check_output([str(x) for x in a],cwd=str(ROOT),text=True).strip()
def clean_for(path):
    rel=str(path.relative_to(ROOT))
    if subprocess.run(['git','diff','--quiet','--',rel],cwd=str(ROOT)).returncode!=0:return False
    if subprocess.run(['git','diff','--cached','--quiet','--',rel],cwd=str(ROOT)).returncode!=0:return False
    return True

if not SOURCE.is_file(): raise SystemExit(PREFIX+' tracked observer missing')
try: out(['git','ls-files','--error-unmatch',str(SOURCE.relative_to(ROOT))])
except subprocess.CalledProcessError: raise SystemExit(PREFIX+' observer is not tracked')
if not clean_for(SOURCE): raise SystemExit(PREFIX+' tracked observer modified; refusing materialization')
s=SOURCE.read_text()
if MARKER not in s: raise SystemExit(PREFIX+' observer marker missing')

# Materialization hotfix: System.Array.GetValue(int) is rank-1 only. Simplex.grad3 may be
# multidimensional, so hash arrays through IEnumerable enumeration instead of linear GetValue.
old='''        static string ArraySha(Array a)\n        {\n            StringBuilder sb=new StringBuilder();\n            for(int i=0;i<a.Length;i++)\n            {\n                object v=a.GetValue(i);\n                sb.Append(v==null?"NULL":FormatValue(v)); sb.Append('\\n');\n            }\n            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));\n        }\n'''
new='''        static string ArraySha(Array a)\n        {\n            StringBuilder sb=new StringBuilder();\n            foreach(object v in a)\n            {\n                sb.Append(v==null?"NULL":FormatValue(v)); sb.Append('\\n');\n            }\n            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));\n        }\n'''
if old not in s: raise SystemExit(PREFIX+' ArraySha hotfix anchor missing')
s=s.replace(old,new,1)
SOURCE.write_text(s)
print(PREFIX+' PASS multidimensional-array-safe observer materialization')

cs=CSPROJ.read_text()
inc='    <Compile Include="Terrain\\AERISR038PtcMinmusVertexPlanetDependencyClosureObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR037PtcPolFlattenOceanExactClosureObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs: raise SystemExit(PREFIX+' accepted R037 csproj anchor missing')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))

version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit(PREFIX+' accepted R037 parent identity missing')
version=version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS33 REV3.5 R037 POL FLATTEN OCEAN EXACT CLOSURE SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS33 REV3.5 R038 MINMUS VERTEXPLANET DEPENDENCY CLOSURE SHADOW'
).replace(PARENT,MARKER)

head=out(['git','rev-parse','HEAD'])
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
print(PREFIX+' PASS')
print('head='+head)
print('source_sha256='+hashlib.sha256(SOURCE.read_bytes()).hexdigest())
print('source_tree_sha256='+h.hexdigest())
