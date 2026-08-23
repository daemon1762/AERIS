#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'Source/AERISFlightControl/Terrain'
SOURCE=SRC/'AERISR031PtcGillyDependencyClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW'
MARKER='AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW'
OBSERVER=r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW
    // Observation only. Closes the pure-math dependency set required by the Gilly clone.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcGillyDependencyClosureObserver : MonoBehaviour
    {
        float nextAttempt; bool captured;
        void Update()
        {
            if (captured || Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (!AERISTerrainTileSystem.GameDataHashReady || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            Capture(); captured=true;
        }
        void Capture()
        {
            int types=0,methods=0,withBody=0,instructions=0,arrays=0,arrayValues=0,failures=0;
            Assembly lib=typeof(LibNoise.RidgedMultifractal).Assembly;
            string[] names={"LibNoise.GradientNoiseBasis","LibNoise.Utils"};
            for(int i=0;i<names.Length;i++)
            {
                Type t=lib.GetType(names[i],false);
                if(t==null){ failures++; AERISLogger.Info("[R031][PTC_GILLY_DEP_FAIL] missing_type="+names[i]+"; authority=PQS"); continue; }
                DumpType(t,ref types,ref methods,ref withBody,ref instructions,ref arrays,ref arrayValues,ref failures);
            }
            DumpGillyArrays(ref arrays,ref arrayValues,ref failures);
            AERISLogger.Info("[R031][PTC_GILLY_DEP] event=DEPENDENCY_CLOSURE_COMPLETE; types="+types+
                "; methods="+methods+"; methods_with_body="+withBody+"; instructions="+instructions+
                "; arrays="+arrays+"; array_values="+arrayValues+"; failures="+failures+
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false"+
                "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }
        static void DumpType(Type t,ref int types,ref int methods,ref int withBody,ref int instructions,ref int arrays,ref int arrayValues,ref int failures)
        {
            types++;
            DumpStaticArrays(t,ref arrays,ref arrayValues,ref failures);
            MethodInfo decoder=typeof(AERISR031PtcGillyIlDisassemblyObserver).GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic);
            MethodInfo[] ms=t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            Array.Sort(ms,(a,b)=>string.CompareOrdinal(a.Name,b.Name));
            for(int i=0;i<ms.Length;i++)
            {
                MethodInfo m=ms[i]; if(m==null || !Wanted(m)) continue; methods++;
                try
                {
                    MethodBody b=m.GetMethodBody(); byte[] il=b==null?null:b.GetILAsByteArray();
                    AERISLogger.Info("[R031][PTC_GILLY_DEP_METHOD] type="+Safe(t.FullName)+"; method="+Safe(Signature(m))+
                        "; body_present="+(il!=null)+"; il_bytes="+(il==null?0:il.Length)+"; authority=PQS");
                    if(il==null) continue; withBody++;
                    int localFailures=0; object[] args={m,il,localFailures};
                    var decoded=decoder==null?null:decoder.Invoke(null,args) as List<string>;
                    if(args[2] is int) localFailures=(int)args[2]; failures+=localFailures;
                    if(decoded==null){ failures++; continue; }
                    instructions+=decoded.Count;
                    const int PerChunk=18; int chunk=0;
                    for(int p=0;p<decoded.Count;p+=PerChunk)
                    {
                        int take=Math.Min(PerChunk,decoded.Count-p); string[] part=new string[take];
                        for(int j=0;j<take;j++) part[j]=decoded[p+j];
                        AERISLogger.Info("[R031][PTC_GILLY_DEP_IL] type="+Safe(t.FullName)+"; method="+Safe(m.Name)+
                            "; chunk="+(chunk++)+"; instructions="+Safe(string.Join("/",part))+"; authority=PQS");
                    }
                }
                catch(Exception ex){ failures++; AERISLogger.Info("[R031][PTC_GILLY_DEP_FAIL] type="+Safe(t.FullName)+"; method="+Safe(m.Name)+"; error="+ex.GetType().Name+"; authority=PQS"); }
            }
        }
        static bool Wanted(MethodInfo m)
        {
            string n=m.Name;
            return n.Contains("Gradient") || n.Contains("Interp") || n.Contains("Curve") || n.Contains("Range") || n=="Floor" || n=="MakeInt32Range";
        }
        static void DumpStaticArrays(Type t,ref int arrays,ref int values,ref int failures)
        {
            FieldInfo[] fs=t.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<fs.Length;i++)
            {
                FieldInfo f=fs[i]; object v=null; try{v=f.GetValue(null);}catch{failures++;continue;}
                DumpArray(t.FullName+"."+f.Name,v,ref arrays,ref values);
            }
        }
        static void DumpGillyArrays(ref int arrays,ref int values,ref int failures)
        {
            CelestialBody g=null;
            for(int i=0;i<FlightGlobals.Bodies.Count;i++){var b=FlightGlobals.Bodies[i];if(b!=null&&string.Equals(b.name,"Gilly",StringComparison.OrdinalIgnoreCase)){g=b;break;}}
            if(g==null){failures++;return;}
            object pqs=ReadMember(g,"pqsController"); IEnumerable mods=pqs==null?null:ReadMember(pqs,"mods") as IEnumerable; if(mods==null){failures++;return;}
            foreach(object mod in mods)
            {
                if(mod==null) continue; string tn=mod.GetType().FullName??mod.GetType().Name;
                if(tn=="PQSMod_VertexSimplexHeightAbsolute")
                {
                    object simplex=ReadMember(mod,"simplex");
                    if(simplex!=null)
                    {
                        DumpArray("Simplex.perm",ReadMember(simplex,"perm"),ref arrays,ref values);
                        FieldInfo grad=simplex.GetType().GetField("grad3",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                        if(grad!=null) DumpArray("Simplex.grad3",grad.GetValue(null),ref arrays,ref values);
                    }
                }
                else if(tn=="PQSMod_VertexHeightNoise")
                {
                    object noise=ReadMember(mod,"noiseMap"); if(noise!=null) DumpArray("RidgedMultifractal.SpectralWeights",ReadMember(noise,"SpectralWeights"),ref arrays,ref values);
                }
            }
        }
        static void DumpArray(string name,object value,ref int arrays,ref int values)
        {
            Array a=value as Array; if(a==null) return; arrays++;
            var flat=new List<string>(); Flatten(a,flat); values+=flat.Count;
            const int PerChunk=48; int chunk=0;
            for(int p=0;p<flat.Count;p+=PerChunk)
            {
                int take=Math.Min(PerChunk,flat.Count-p); string[] part=new string[take]; for(int j=0;j<take;j++)part[j]=flat[p+j];
                AERISLogger.Info("[R031][PTC_GILLY_DEP_DATA] name="+Safe(name)+"; length="+flat.Count+"; chunk="+(chunk++)+"; values="+Safe(string.Join(",",part))+"; authority=PQS");
            }
        }
        static void Flatten(Array a,List<string> dest)
        {
            for(int i=0;i<a.Length;i++)
            {
                object v=a.GetValue(i); Array nested=v as Array;
                if(nested!=null){Flatten(nested,dest);continue;}
                if(v==null) dest.Add("null"); else if(v is IFormattable) dest.Add(((IFormattable)v).ToString(null,CultureInfo.InvariantCulture)); else dest.Add(v.ToString());
            }
        }
        static object ReadMember(object target,string name)
        {
            if(target==null)return null; Type t=target.GetType(); FieldInfo f=t.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); if(f!=null)return f.GetValue(f.IsStatic?null:target);
            PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); return p!=null&&p.CanRead&&p.GetIndexParameters().Length==0?p.GetValue(p.GetGetMethod(true).IsStatic?null:target,null):null;
        }
        static string Signature(MethodInfo m){ParameterInfo[] p=m.GetParameters();string[] a=new string[p.Length];for(int i=0;i<p.Length;i++)a[i]=p[i].ParameterType.FullName;return m.Name+"("+string.Join(",",a)+")->"+m.ReturnType.FullName;}
        static string Safe(string s){return string.IsNullOrEmpty(s)?string.Empty:s.Replace('\n',' ').Replace('\r',' ').Replace(';',',').Replace('|','/');}
    }
}
'''
SOURCE.parent.mkdir(parents=True,exist_ok=True)
if SOURCE.exists() and SOURCE.read_text()!=OBSERVER: raise SystemExit('R031 Gilly dependency closure observer exists with unexpected content')
SOURCE.write_text(OBSERVER)
cs=CSPROJ.read_text(); inc='    <Compile Include="Terrain\\AERISR031PtcGillyDependencyClosureObserver.cs" />\n'; anchor='    <Compile Include="Terrain\\AERISR031PtcGillyIlDisassemblyObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs: raise SystemExit('R031 Gilly IL csproj anchor missing')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))
version=VERSION.read_text()
if PARENT not in version and MARKER not in version: raise SystemExit('R031 Gilly IL parent identity missing')
version=version.replace('AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY IL DISASSEMBLY SHADOW','AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY DEPENDENCY CLOSURE SHADOW').replace(PARENT,MARKER)
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip(); h=hashlib.sha256(); files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for p in files:
    if p==VERSION: continue
    h.update(str(p.relative_to(ROOT)).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";','internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";','internal const string SourceTreeSha256 = "'+h.hexdigest()+'";',version)
VERSION.write_text(version)
print('PASS apply R031 Gilly dependency closure shadow')
print('head='+head)
