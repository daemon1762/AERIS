using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS33 R038: exact dependency/helper closure for stock Minmus PQSMod_VertexPlanet.
    // This observer is intentionally shadow-only. Runtime KSP/PQS objects are read only on the
    // main thread. No runtime object is ever passed to a worker and Minmus remains fail-closed
    // until the captured closure is reconstructed and compared against PQS witnesses.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR038PtcMinmusVertexPlanetDependencyClosureObserver : MonoBehaviour
    {
        internal const string CandidateMarker =
            "AERIS33_REV3_5_R038_PTC_MINMUS_VERTEXPLANET_DEPENDENCY_CLOSURE_SHADOW";
        const string ExpectedMainIlSha =
            "513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687";

        struct Point3
        {
            internal double X, Y, Z;
            internal Point3(double x, double y, double z) { X=x; Y=y; Z=z; }
        }

        bool done;
        float nextAttempt;

        void Update()
        {
            if (done) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (!AERISTerrainTileSystem.GameDataHashReady) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            done = true;
            try { Audit(); }
            catch (Exception ex)
            {
                AERISLogger.Error("[R038][VERTEXPLANET_FAIL] stage=AUDIT; error="+
                    Safe(ex.GetType().FullName+":"+ex.Message)+
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                    "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY"+
                    "; db_write=false; producer_switch=false; gpu=false; authority=PQS");
            }
        }

        void Audit()
        {
            CelestialBody body = FindBody("Minmus");
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException("Minmus body/PQS missing");

            object mod = FindVertexPlanet(body.pqsController);
            if (mod == null) throw new InvalidOperationException("Minmus PQSMod_VertexPlanet missing");
            Type mt = mod.GetType();
            MethodInfo main = mt.GetMethod("OnVertexBuildHeight",
                BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if (main == null) throw new InvalidOperationException("VertexPlanet OnVertexBuildHeight missing");
            string mainSha = MethodIlSha(main);
            if (!string.Equals(mainSha, ExpectedMainIlSha, StringComparison.Ordinal))
                throw new InvalidOperationException("VertexPlanet main IL hash mismatch "+mainSha);

            double pqsRadius = ReadDoubleOrDefault(body.pqsController, "radius", body.Radius);
            AERISLogger.Info("[R038][VERTEXPLANET] event=SNAPSHOT_BEGIN; body=Minmus"+
                "; runtime_type="+Safe(mt.FullName??mt.Name)+
                "; main_il_sha256="+mainSha+"; body_radius="+F(body.Radius)+
                "; pqs_radius="+F(pqsRadius)+
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                "; worker_invokes_runtime_object=false; authority=PQS");

            string[] configScalars = new string[] {
                "deformity", "oceanLevel", "oceanSnap", "oceanDepth", "oceanStep",
                "terrainRidgeBalance", "terrainRidgesMin", "terrainRidgesMax",
                "terrainShapeStart", "terrainShapeEnd"
            };
            for (int i=0;i<configScalars.Length;i++) LogNamedMember(mod, "VERTEXPLANET", configScalars[i]);

            string[] simplexWrappers = new string[] {
                "continental", "continentalSmoothing", "continentalSharpnessMap", "continentalRuggedness"
            };
            int wrapperCount = 0;
            for (int i=0;i<simplexWrappers.Length;i++)
            {
                object wrapper = ReadMember(mod, simplexWrappers[i]);
                if (wrapper == null) throw new InvalidOperationException(simplexWrappers[i]+" wrapper missing");
                wrapperCount++;
                LogObjectMembers("WRAPPER", simplexWrappers[i], wrapper);
                LogGetterIl(wrapper.GetType(), "simplex", simplexWrappers[i]);
                object simplex = ReadMember(wrapper, "simplex");
                if (simplex == null) throw new InvalidOperationException(simplexWrappers[i]+" simplex missing");
                LogObjectMembers("SIMPLEX", simplexWrappers[i], simplex);
                LogArrayMember("SIMPLEX_ARRAY", simplexWrappers[i], simplex, "perm");
                FieldInfo grad = simplex.GetType().GetField("grad3",
                    BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy);
                if (grad != null) LogArrayValue("SIMPLEX_ARRAY", simplexWrappers[i], "grad3", grad.GetValue(null));
                LogNativeSamples(simplexWrappers[i], simplex, "noise");
                LogNativeSamples(simplexWrappers[i], simplex, "noiseNormalized");
            }

            object sharpWrapper = ReadMember(mod, "continentalSharpness");
            if (sharpWrapper == null) throw new InvalidOperationException("continentalSharpness wrapper missing");
            wrapperCount++;
            LogObjectMembers("WRAPPER", "continentalSharpness", sharpWrapper);
            LogGetterIl(sharpWrapper.GetType(), "noise", "continentalSharpness");
            object noise = ReadMember(sharpWrapper, "noise");
            if (noise == null) throw new InvalidOperationException("continentalSharpness noise missing");
            LogObjectMembers("NOISE", "continentalSharpness", noise);
            LogAllArrays("NOISE_ARRAY", "continentalSharpness", noise);
            LogConcreteMethodIl(noise.GetType(), "GetValue", "continentalSharpness.GetValue");
            LogNativeSamples("continentalSharpness", noise, "GetValue");

            LogHelper(mt, "Lerp");
            LogHelper(mt, "Clamp");
            LogHelper(mt, "CubicHermite");

            Point3[] witness = BuildTerrainPoints();
            MethodInfo surface = FindVector3dMethod(body.pqsController.GetType(), "GetSurfaceHeight");
            if (surface == null) throw new InvalidOperationException("PQS GetSurfaceHeight(Vector3d) missing");
            for (int i=0;i<witness.Length;i++)
            {
                Point3 p = witness[i];
                object raw = surface.Invoke(body.pqsController,
                    new object[] { new Vector3d(p.X,p.Y,p.Z) });
                double h = Convert.ToDouble(raw, CultureInfo.InvariantCulture)-pqsRadius;
                AERISLogger.Info("[R038][PQS_WITNESS] body=Minmus; index="+i+
                    "; x="+F(p.X)+"; y="+F(p.Y)+"; z="+F(p.Z)+
                    "; terrain_m="+F(h)+"; authority=PQS");
            }

            AERISLogger.Info("[R038][VERTEXPLANET] event=DEPENDENCY_CLOSURE_COMPLETE"+
                "; body=Minmus; wrappers="+wrapperCount+"; simplex_wrappers=4; noise_wrappers=1"+
                "; helper_methods=3; witness_points="+witness.Length+
                "; failures=0; worker_ready=false"+
                "; pending=PQSMod_VertexPlanet:PURE_CPU_FORMULA_RECONSTRUCTION_PENDING"+
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY"+
                "; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static CelestialBody FindBody(string name)
        {
            for (int i=0;i<FlightGlobals.Bodies.Count;i++)
            {
                CelestialBody b=FlightGlobals.Bodies[i];
                if (b!=null && string.Equals(b.bodyName,name,StringComparison.Ordinal)) return b;
            }
            return null;
        }

        static object FindVertexPlanet(object pqs)
        {
            IEnumerable mods=ReadMember(pqs,"mods") as IEnumerable;
            if (mods==null) return null;
            foreach(object mod in mods)
            {
                if(mod==null) continue;
                Type t=mod.GetType();
                string n=t.FullName??t.Name;
                if(n=="PQSMod_VertexPlanet") return mod;
            }
            return null;
        }

        static void LogNamedMember(object obj,string scope,string name)
        {
            object v=ReadMember(obj,name);
            if(v==null)
            {
                AERISLogger.Info("[R038][SCALAR] scope="+scope+"; name="+name+"; value=NULL");
                return;
            }
            AERISLogger.Info("[R038][SCALAR] scope="+scope+"; name="+name+
                "; type="+Safe(v.GetType().FullName??v.GetType().Name)+"; value="+Safe(FormatValue(v)));
        }

        static void LogObjectMembers(string eventName,string label,object obj)
        {
            Type t=obj.GetType();
            AERISLogger.Info("[R038]["+eventName+"] label="+label+
                "; runtime_type="+Safe(t.FullName??t.Name));
            FieldInfo[] fs=t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Array.Sort(fs,delegate(FieldInfo a,FieldInfo b){return string.CompareOrdinal(a.Name,b.Name);});
            for(int i=0;i<fs.Length;i++)
            {
                object v;
                try { v=fs[i].GetValue(obj); } catch { continue; }
                if(v==null || IsScalar(v.GetType()))
                {
                    AERISLogger.Info("[R038][MEMBER] label="+label+"; kind=FIELD; name="+fs[i].Name+
                        "; type="+Safe(fs[i].FieldType.FullName??fs[i].FieldType.Name)+
                        "; value="+Safe(v==null?"NULL":FormatValue(v)));
                }
                else if(v is Array)
                {
                    LogArrayValue("MEMBER_ARRAY",label,fs[i].Name,v);
                }
                else
                {
                    AERISLogger.Info("[R038][MEMBER] label="+label+"; kind=FIELD; name="+fs[i].Name+
                        "; type="+Safe(v.GetType().FullName??v.GetType().Name)+"; value=OBJECT");
                }
            }
        }

        static void LogArrayMember(string evt,string label,object obj,string name)
        {
            object v=ReadMember(obj,name);
            LogArrayValue(evt,label,name,v);
        }

        static void LogAllArrays(string evt,string label,object obj)
        {
            Type t=obj.GetType();
            FieldInfo[] fs=t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<fs.Length;i++)
            {
                object v;
                try { v=fs[i].GetValue(obj); } catch { continue; }
                if(v is Array) LogArrayValue(evt,label,fs[i].Name,v);
            }
        }

        static void LogArrayValue(string evt,string label,string name,object value)
        {
            Array a=value as Array;
            if(a==null)
            {
                AERISLogger.Info("[R038]["+evt+"] label="+label+"; name="+name+"; state=MISSING");
                return;
            }
            AERISLogger.Info("[R038]["+evt+"] label="+label+"; name="+name+
                "; length="+a.Length+"; sha256="+ArraySha(a));
        }

        static void LogGetterIl(Type wrapperType,string propertyName,string label)
        {
            PropertyInfo p=wrapperType.GetProperty(propertyName,
                BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            MethodInfo m=p==null?null:p.GetGetMethod(true);
            if(m==null)
            {
                MethodInfo[] ms=wrapperType.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                for(int i=0;i<ms.Length;i++)
                    if(ms[i].Name=="get_"+propertyName){m=ms[i];break;}
            }
            LogMethodIl("GETTER_IL",label+".get_"+propertyName,m);
        }

        static void LogConcreteMethodIl(Type t,string methodName,string label)
        {
            MethodInfo selected=null;
            MethodInfo[] ms=t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<ms.Length;i++)
            {
                if(!(ms[i].Name==methodName || ms[i].Name.EndsWith("."+methodName,StringComparison.Ordinal))) continue;
                if(ms[i].GetMethodBody()!=null){selected=ms[i];break;}
            }
            LogMethodIl("NOISE_IL",label,selected);
        }

        static void LogHelper(Type t,string name)
        {
            MethodInfo selected=null;
            MethodInfo[] ms=t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<ms.Length;i++)
            {
                if(ms[i].Name!=name || ms[i].GetMethodBody()==null) continue;
                selected=ms[i];break;
            }
            if(selected==null) throw new InvalidOperationException("helper missing "+name);
            LogMethodIl("HELPER_IL",name,selected);
            ParameterInfo[] ps=selected.GetParameters();
            bool allDouble=selected.ReturnType==typeof(double);
            for(int i=0;i<ps.Length;i++) if(ps[i].ParameterType!=typeof(double)) allDouble=false;
            if(!allDouble) return;
            for(int sample=0;sample<3;sample++)
            {
                object[] args=new object[ps.Length];
                for(int i=0;i<args.Length;i++) args[i]=0.125*(sample+1)*(i+1);
                object r=selected.Invoke(selected.IsStatic?null:null,args);
                StringBuilder sb=new StringBuilder();
                for(int i=0;i<args.Length;i++){if(i!=0)sb.Append(',');sb.Append(F((double)args[i]));}
                AERISLogger.Info("[R038][HELPER_WITNESS] method="+name+"; sample="+sample+
                    "; args="+sb.ToString()+"; result="+F(Convert.ToDouble(r,CultureInfo.InvariantCulture)));
            }
        }

        static void LogMethodIl(string evt,string label,MethodInfo m)
        {
            if(m==null)
            {
                AERISLogger.Error("[R038][VERTEXPLANET_FAIL] stage="+evt+"; error=METHOD_MISSING; label="+label);
                return;
            }
            MethodBody body=m.GetMethodBody();
            if(body==null)
            {
                AERISLogger.Error("[R038][VERTEXPLANET_FAIL] stage="+evt+"; error=METHOD_BODY_MISSING; label="+label);
                return;
            }
            byte[] il=body.GetILAsByteArray(); if(il==null)il=new byte[0];
            ParameterInfo[] ps=m.GetParameters();
            StringBuilder sig=new StringBuilder();
            for(int i=0;i<ps.Length;i++){if(i!=0)sig.Append(',');sig.Append(ps[i].ParameterType.FullName??ps[i].ParameterType.Name);}
            AERISLogger.Info("[R038]["+evt+"] label="+label+
                "; declaring_type="+Safe(m.DeclaringType==null?"NULL":(m.DeclaringType.FullName??m.DeclaringType.Name))+
                "; return_type="+Safe(m.ReturnType.FullName??m.ReturnType.Name)+
                "; params="+Safe(sig.ToString())+"; code_size="+il.Length+
                "; il_sha256="+Sha256(il)+"; il_hex="+Hex(il));
        }

        static void LogNativeSamples(string label,object obj,string methodName)
        {
            Point3[] pts=BuildPrimitivePoints();
            for(int i=0;i<pts.Length;i++)
            {
                double v;
                if(!TryInvokePointMethod(obj,methodName,pts[i],out v))
                {
                    if(i==0) AERISLogger.Info("[R038][NATIVE] label="+label+"; method="+methodName+"; state=METHOD_NOT_COMPATIBLE");
                    return;
                }
                AERISLogger.Info("[R038][NATIVE] label="+label+"; method="+methodName+"; index="+i+
                    "; x="+F(pts[i].X)+"; y="+F(pts[i].Y)+"; z="+F(pts[i].Z)+"; value="+F(v));
            }
        }

        static bool TryInvokePointMethod(object obj,string methodName,Point3 p,out double value)
        {
            MethodInfo[] ms=obj.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<ms.Length;i++)
            {
                if(!(ms[i].Name==methodName || ms[i].Name.EndsWith("."+methodName,StringComparison.Ordinal))) continue;
                ParameterInfo[] ps=ms[i].GetParameters();
                try
                {
                    object r=null;
                    if(ps.Length==3 && ps[0].ParameterType==typeof(double) && ps[1].ParameterType==typeof(double) && ps[2].ParameterType==typeof(double))
                        r=ms[i].Invoke(obj,new object[]{p.X,p.Y,p.Z});
                    else if(ps.Length==1 && ps[0].ParameterType==typeof(Vector3d))
                        r=ms[i].Invoke(obj,new object[]{new Vector3d(p.X,p.Y,p.Z)});
                    else continue;
                    value=Convert.ToDouble(r,CultureInfo.InvariantCulture); return true;
                }
                catch { }
            }
            value=0.0; return false;
        }

        static Point3[] BuildPrimitivePoints()
        {
            return new Point3[] {
                new Point3(0,0,0), new Point3(0.1,0.2,0.3),
                new Point3(-0.25,0.5,-0.75), new Point3(1,2,3),
                new Point3(12.345,-67.89,0.125), new Point3(-Math.PI,Math.E,0.577215664901533)
            };
        }

        static Point3[] BuildTerrainPoints()
        {
            Point3[] p=new Point3[] {
                new Point3(1,0,0),new Point3(0,1,0),new Point3(0,0,1),
                new Point3(-1,0,0),new Point3(0,-1,0),new Point3(0,0,-1),
                new Point3(1,1,1),new Point3(-1,1,1),new Point3(1,-1,1),
                new Point3(1,1,-1),new Point3(0.123,-0.456,0.789),new Point3(-0.731,0.419,0.538)
            };
            for(int i=0;i<p.Length;i++)
            {
                double m=Math.Sqrt(p[i].X*p[i].X+p[i].Y*p[i].Y+p[i].Z*p[i].Z);
                p[i]=new Point3(p[i].X/m,p[i].Y/m,p[i].Z/m);
            }
            return p;
        }

        static MethodInfo FindVector3dMethod(Type t,string name)
        {
            MethodInfo[] ms=t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<ms.Length;i++)
            {
                if(ms[i].Name!=name) continue;
                ParameterInfo[] ps=ms[i].GetParameters();
                if(ps.Length==1 && ps[0].ParameterType==typeof(Vector3d)) return ms[i];
            }
            return null;
        }

        static object ReadMember(object obj,string name)
        {
            if(obj==null) return null;
            Type t=obj.GetType();
            while(t!=null)
            {
                FieldInfo f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(f!=null) return f.GetValue(obj);
                PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(p!=null && p.GetIndexParameters().Length==0) return p.GetValue(obj,null);
                t=t.BaseType;
            }
            return null;
        }

        static double ReadDoubleOrDefault(object obj,string name,double fallback)
        {
            object v=ReadMember(obj,name); if(v==null) return fallback;
            return Convert.ToDouble(v,CultureInfo.InvariantCulture);
        }

        static bool IsScalar(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t==typeof(string) || t==typeof(decimal);
        }

        static string FormatValue(object v)
        {
            if(v is double) return ((double)v).ToString("R",CultureInfo.InvariantCulture);
            if(v is float) return ((float)v).ToString("R",CultureInfo.InvariantCulture);
            if(v is IFormattable) return ((IFormattable)v).ToString(null,CultureInfo.InvariantCulture);
            return v==null?"NULL":v.ToString();
        }

        static string MethodIlSha(MethodInfo m)
        {
            MethodBody b=m.GetMethodBody(); if(b==null) return string.Empty;
            byte[] il=b.GetILAsByteArray(); if(il==null)il=new byte[0]; return Sha256(il);
        }

        static string ArraySha(Array a)
        {
            StringBuilder sb=new StringBuilder();
            for(int i=0;i<a.Length;i++)
            {
                object v=a.GetValue(i);
                sb.Append(v==null?"NULL":FormatValue(v)); sb.Append('\n');
            }
            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        static string Sha256(byte[] data)
        {
            using(SHA256 h=SHA256.Create())
            {
                byte[] d=h.ComputeHash(data); StringBuilder sb=new StringBuilder(d.Length*2);
                for(int i=0;i<d.Length;i++) sb.Append(d[i].ToString("x2",CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string Hex(byte[] data)
        {
            StringBuilder sb=new StringBuilder(data.Length*2);
            for(int i=0;i<data.Length;i++) sb.Append(data[i].ToString("x2",CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static string F(double v){return v.ToString("R",CultureInfo.InvariantCulture);}
        static string Safe(string s)
        {
            return string.IsNullOrEmpty(s)?"-":s.Replace('\n',' ').Replace('\r',' ').Replace(';',',').Replace('|','/');
        }
    }
}
