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
if not SOURCE.is_file(): raise SystemExit(PREFIX+' tracked observer missing')
rel=str(SOURCE.relative_to(ROOT))
try: out(['git','ls-files','--error-unmatch',rel])
except subprocess.CalledProcessError: raise SystemExit(PREFIX+' observer is not tracked')
tracked=subprocess.check_output(['git','show','HEAD:'+rel],cwd=str(ROOT),text=True)
if MARKER not in tracked: raise SystemExit(PREFIX+' observer marker missing from tracked source')

# Deterministic materialization. The tracked observer stays reviewable; build-time materialization
# applies only audited safety fixes. Previous Hotfix0 materialization is accepted so interrupted
# builds can be resumed without reset/clean/stash.
old_array='''        static string ArraySha(Array a)\n        {\n            StringBuilder sb=new StringBuilder();\n            for(int i=0;i<a.Length;i++)\n            {\n                object v=a.GetValue(i);\n                sb.Append(v==null?"NULL":FormatValue(v)); sb.Append('\\n');\n            }\n            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));\n        }\n'''
new_array='''        static string ArraySha(Array a)\n        {\n            StringBuilder sb=new StringBuilder();\n            foreach(object v in a)\n            {\n                sb.Append(v==null?"NULL":FormatValue(v)); sb.Append('\\n');\n            }\n            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));\n        }\n'''
old_calls='''            LogHelper(mt, "Lerp");\n            LogHelper(mt, "Clamp");\n            LogHelper(mt, "CubicHermite");\n'''
new_calls='''            LogHelper(mod, mt, "Lerp");\n            LogHelper(mod, mt, "Clamp");\n            LogHelper(mod, mt, "CubicHermite");\n'''
old_signature='''        static void LogHelper(Type t,string name)\n'''
new_signature='''        static void LogHelper(object instance,Type t,string name)\n'''
old_invoke='''                object r=selected.Invoke(selected.IsStatic?null:null,args);\n'''
previous_invoke='''                if(!selected.IsStatic) throw new InvalidOperationException("helper expected static "+name);\n                object r=selected.Invoke(null,args);\n'''
new_invoke='''                object target=null;\n                if(!selected.IsStatic)\n                {\n                    if(instance==null) throw new InvalidOperationException("instance helper target missing "+name);\n                    MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",\n                        BindingFlags.Instance|BindingFlags.NonPublic);\n                    if(clone==null) throw new InvalidOperationException("MemberwiseClone missing");\n                    target=clone.Invoke(instance,null);\n                    AERISLogger.Info("[R038][HELPER_TARGET] method="+name+\n                        "; invocation_target=SHALLOW_CLONE; live_runtime_object_mutated=false"+\n                        "; runtime_object_invocation_thread=MAIN_THREAD_ONLY");\n                }\n                object r=selected.Invoke(target,args);\n'''
for anchor,label in ((old_array,'ArraySha'),(old_calls,'helper calls'),(old_signature,'helper signature'),(old_invoke,'helper invoke')):
    if anchor not in tracked: raise SystemExit(PREFIX+' tracked anchor missing '+label)
previous=tracked.replace(old_array,new_array,1).replace(old_invoke,previous_invoke,1)
materialized=(tracked.replace(old_array,new_array,1)
                    .replace(old_calls,new_calls,1)
                    .replace(old_signature,new_signature,1)
                    .replace(old_invoke,new_invoke,1))
current=SOURCE.read_text()
if current==tracked or current==previous:
    SOURCE.write_text(materialized)
    print(PREFIX+' PASS accepted Hotfix1 observer materialized')
elif current==materialized:
    print(PREFIX+' PASS accepted Hotfix1 observer materialization already present')
else:
    raise SystemExit(PREFIX+' observer differs from tracked/Hotfix0/Hotfix1 accepted forms; refusing')

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
