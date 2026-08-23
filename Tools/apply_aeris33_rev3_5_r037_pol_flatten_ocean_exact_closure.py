#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
PARENT_SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs'
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR037PtcPolFlattenOceanExactClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW'
MARKER='AERIS33_REV3_5_R037_PTC_POL_FLATTEN_OCEAN_EXACT_CLOSURE_SHADOW'
PARENT_SHA='4e045e0123a493179fe986bd9ed746d0732e06f5e8eace38006ff0571ea3ee7f'
FLATTEN_IL_SHA='4b00ff62f5a99eeae99d7236b16a0aa1dfed1d22a6c9cc991d6da38fce55a112'

if not PARENT_SOURCE.is_file(): raise SystemExit('R036 parent observer missing')
actual=hashlib.sha256(PARENT_SOURCE.read_bytes()).hexdigest()
if actual!=PARENT_SHA: raise SystemExit('R036 parent observer SHA256 mismatch expected='+PARENT_SHA+' actual='+actual)
print('PASS R036 parent plain-C# observer SHA256 exact')

s=PARENT_SOURCE.read_text()
replacements=[
('AERIS33 R036: first common pure-procedural CPU-exact worker framework.',
 'AERIS33 R037: Pol FlattenOcean exact formula closure over the accepted R036 common worker.'),
('AERISR036PtcCommonPureCpuExactFormulaClosureObserver','AERISR037PtcPolFlattenOceanExactClosureObserver'),
('AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW',MARKER),
('[R036]','[R037]'),
('event=FORMULA_CLOSURE_WORKER_COMPLETE','event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE'),
('HeightOffset = 4\n','HeightOffset = 4,\n            FlattenOcean = 5\n'),
('internal double Offset;\n','internal double Offset;\n            internal double OceanFloorMeters;\n'),
('if (!supported || tn=="PQSMod_VertexSimplexHeight")\n',
 'if (!supported || tn=="PQSMod_VertexSimplexHeight" || tn=="PQSMod_FlattenOcean")\n'),
]
for old,new in replacements:
    if old not in s: raise SystemExit('R037 transform anchor missing: '+old[:80])
    s=s.replace(old,new) if old=='[R036]' else s.replace(old,new,1)

capture_anchor='''            else if (st.TypeName=="PQSMod_VertexHeightOffset")\n            {\n                st.Offset=ReadDouble(mod,"offset");\n                st.Kind=StepKind.HeightOffset;\n                st.NativePrimitive=new double[0];\n            }\n'''
flatten_capture='''            else if (st.TypeName=="PQSMod_FlattenOcean")\n            {\n                MethodInfo fm=mod.GetType().GetMethod("OnVertexBuildHeight",\n                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);\n                MethodBody fb=fm==null?null:fm.GetMethodBody();\n                byte[] fil=fb==null?null:fb.GetILAsByteArray();\n                string fsha=fil==null?string.Empty:Sha256(fil);\n                if (fsha=="'''+FLATTEN_IL_SHA+'''")\n                {\n                    object sphere=ReadMember(mod,"sphere");\n                    if (sphere==null) throw new InvalidOperationException("FlattenOcean sphere missing");\n                    double oceanRad=ReadDouble(mod,"oceanRad");\n                    double sphereRadius=ReadDouble(sphere,"radius");\n                    st.OceanFloorMeters=oceanRad-sphereRadius;\n                    st.Kind=StepKind.FlattenOcean;\n                    st.NativePrimitive=new double[0];\n                    AERISLogger.Info("[R037][FLATTEN] oceanRad="+F(oceanRad)+\n                        "; sphere_radius="+F(sphereRadius)+"; relative_floor_m="+F(st.OceanFloorMeters)+\n                        "; il_sha256="+fsha+"; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+\n                        "; worker_invokes_runtime_object=false; authority=PQS");\n                }\n            }\n'''
if capture_anchor not in s: raise SystemExit('R037 CaptureStep anchor missing')
s=s.replace(capture_anchor,flatten_capture+capture_anchor,1)

apply_anchor='''            if (st.Kind==StepKind.HeightOffset)\n                return h+st.Offset;\n            throw new InvalidOperationException("unsupported worker step "+st.TypeName);\n'''
flatten_apply='''            if (st.Kind==StepKind.FlattenOcean)\n                return h<st.OceanFloorMeters?st.OceanFloorMeters:h;\n'''
if apply_anchor not in s: raise SystemExit('R037 ApplyStep anchor missing')
s=s.replace(apply_anchor,flatten_apply+apply_anchor,1)

SOURCE.write_text(s)
print('PASS generated R037 Pol FlattenOcean exact-closure observer')
print('source_sha256='+hashlib.sha256(SOURCE.read_bytes()).hexdigest())

cs=CSPROJ.read_text()
inc='    <Compile Include="Terrain\\AERISR037PtcPolFlattenOceanExactClosureObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs: raise SystemExit('R036 observer csproj anchor missing; materialize accepted R036 first')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))

version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R036 parent identity missing; materialize/build accepted R036 first')
version=version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS33 REV3.5 R036 COMMON PURE CPU EXACT FORMULA CLOSURE SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS33 REV3.5 R037 POL FLATTEN OCEAN EXACT CLOSURE SHADOW'
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
print('PASS apply R037 Pol FlattenOcean exact closure shadow')
print('flatten_il_sha256='+FLATTEN_IL_SHA)
print('head='+head)
