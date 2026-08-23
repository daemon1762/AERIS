#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR031PtcGillyDependencyClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW'
if not SOURCE.is_file(): raise SystemExit('R031 Gilly dependency closure source missing')
text=SOURCE.read_text()
if MARKER not in text: raise SystemExit('R031 Gilly dependency closure marker missing')
if 'LibNoise.Utils' in text:
    text=text.replace('LibNoise.Utils','LibNoise.Math')
    SOURCE.write_text(text)
elif 'LibNoise.Math' not in text:
    raise SystemExit('R031 Gilly dependency closure expected LibNoise dependency target missing')
version=VERSION.read_text()
if MARKER not in version: raise SystemExit('R031 Gilly dependency closure build identity missing')
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip()
h=hashlib.sha256(); files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for p in files:
    if p==VERSION: continue
    h.update(str(p.relative_to(ROOT)).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";','internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";','internal const string SourceTreeSha256 = "'+h.hexdigest()+'";',version)
VERSION.write_text(version)
print('PASS apply R031 Gilly dependency closure Hotfix1 Math type')
print('head='+head)
print('dependency_type=LibNoise.Math')
